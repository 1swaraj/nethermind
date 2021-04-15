//  Copyright (c) 2021 Demerzel Solutions Limited
//  This file is part of the Nethermind library.
// 
//  The Nethermind library is free software: you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
// 
//  The Nethermind library is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
//  GNU Lesser General Public License for more details.
// 
//  You should have received a copy of the GNU Lesser General Public License
//  along with the Nethermind. If not, see <http://www.gnu.org/licenses/>.
// 

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Nethermind.Blockchain;
using Nethermind.Blockchain.Processing;
using Nethermind.Blockchain.Producers;
using Nethermind.Blockchain.Rewards;
using Nethermind.Blockchain.Validators;
using Nethermind.Consensus;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Specs;
using Nethermind.Core.Test;
using Nethermind.Core.Test.Blockchain;
using Nethermind.Core.Test.Builders;
using Nethermind.Crypto;
using Nethermind.JsonRpc;
using Nethermind.Logging;
using Nethermind.Db;
using Nethermind.Db.Blooms;
using Nethermind.Merge.Plugin.Data;
using Nethermind.Merge.Plugin.Handlers;
using Nethermind.Specs;
using Nethermind.State.Repositories;
using Nethermind.Serialization.Rlp;
using Nethermind.State;
using NUnit.Framework;
using Result = Nethermind.Merge.Plugin.Data.Result;
using Nethermind.Int256;
using Nethermind.JsonRpc.Test.Modules;
using Nethermind.Specs.Forks;

namespace Nethermind.Merge.Plugin.Test
{
    public class ConsensusModuleTests
    {
        private MergeTestBlockchain _chain;
        private IConsensusRpcModule _consensusRpcModule;

        [SetUp]
        public async Task Setup()
        {
            _chain = await CreateBlockChain();
            _consensusRpcModule = CreateConsensusModule(_chain);
        }
        
        [Test]
        public async Task assembleBlock_should_create_block_on_top_of_genesis()
        {
            IBlockTree blockTree = _chain.BlockTree;
            Keccak startingHead = blockTree.HeadHash;
            ResultWrapper<BlockRequestResult> response = await _consensusRpcModule.consensus_assembleBlock(new AssembleBlockRequest()
            {
                ParentHash = startingHead,
                Timestamp = UInt256.Zero
            });
            startingHead.Should().Be(response.Data.ParentHash);
        }
        
        [Test]
        public async Task assembleBlock_should_not_create_block_with_unknown_parent()
        {
            Keccak notExistingHash = TestItem.KeccakH;
            ResultWrapper<BlockRequestResult> response = await _consensusRpcModule.consensus_assembleBlock(new AssembleBlockRequest()
            {
                ParentHash = notExistingHash,
                Timestamp = UInt256.Zero
            });
            response.Data.Should().BeNull();
        }
        
        [Test]
        public async Task newBlock_accepts_previously_assembled_block()
        {
            IBlockTree blockTree = _chain.BlockTree;
            Keccak startingHead = blockTree.HeadHash;
            BlockHeader startingBestSuggestedHeader = blockTree.BestSuggestedHeader;
            ResultWrapper<BlockRequestResult> assembleBlockResult = await _consensusRpcModule.consensus_assembleBlock(new AssembleBlockRequest()
            {
                ParentHash = startingHead,
                Timestamp = UInt256.Zero
            });
            assembleBlockResult.Data.ParentHash.Should().Be(startingHead);
            
            ResultWrapper<NewBlockResult> newBlockResult = await _consensusRpcModule.consensus_newBlock(assembleBlockResult.Data);
            newBlockResult.Data.Valid.Should().BeTrue();
            
            Keccak bestSuggestedHeaderHash = blockTree.BestSuggestedHeader!.Hash;
            bestSuggestedHeaderHash.Should().Be(assembleBlockResult.Data.BlockHash);
            bestSuggestedHeaderHash.Should().NotBe(startingBestSuggestedHeader!.Hash);
        } 
        
        [Test]
        public async Task setHead_should_changeHead()
        {
            IBlockTree blockTree = _chain.BlockTree;
            Keccak startingHead = blockTree.HeadHash;

            BlockRequestResult blockRequestResult = CreateBlockRequest(
                CreateParentBlockRequestOnHead(), 
                TestItem.AddressD);
            ResultWrapper<NewBlockResult> newBlockResult = await _consensusRpcModule.consensus_newBlock(blockRequestResult);
            newBlockResult.Data.Valid.Should().BeTrue();
            
            Keccak newHeadHash = blockRequestResult.BlockHash;
            ResultWrapper<Result> setHeadResult = await _consensusRpcModule.consensus_setHead(newHeadHash!);
            setHeadResult.Data.Should().Be(Result.Success);
            
            Keccak actualHead = blockTree.HeadHash;
            actualHead.Should().NotBe(startingHead);
            actualHead.Should().Be(newHeadHash); 
        }

        [Test]
        public async Task consensus_finaliseBlock_should_succeed()
        {
            ResultWrapper<Result> resultWrapper = await _consensusRpcModule.consensus_finaliseBlock(TestItem.KeccakE);
            resultWrapper.Data.Should().Be(Result.Success);
        }
        
        [Test]
        public async Task consensus_newBlock_accepts_first_block()
        {
            BlockRequestResult blockRequestResult = CreateBlockRequest(
                CreateParentBlockRequestOnHead(), 
                TestItem.AddressD);
            ResultWrapper<NewBlockResult> resultWrapper = await _consensusRpcModule.consensus_newBlock(blockRequestResult);
            resultWrapper.Data.Valid.Should().BeTrue();
            new BlockRequestResult(_chain.BlockTree.BestSuggestedBody).Should().BeEquivalentTo(blockRequestResult);
        }

        private BlockRequestResult CreateParentBlockRequestOnHead()
        {
            Block head = _chain.BlockTree.Head;
            if (head == null) throw new NotSupportedException();

            return new BlockRequestResult(true) {Number = 0, BlockHash = head.Hash, StateRoot = head.StateRoot, ReceiptsRoot = head.ReceiptsRoot};
        }

        private static BlockRequestResult CreateBlockRequest(BlockRequestResult parent, Address miner)
        {
            BlockRequestResult blockRequest = new BlockRequestResult(true)
            {
                ParentHash = parent.BlockHash,
                Miner = miner,
                StateRoot = parent.StateRoot,
                Number = parent.Number + 1,
                GasLimit = 1_000_000,
                GasUsed = 0,
                ReceiptsRoot = Keccak.EmptyTreeHash,
                LogsBloom = Bloom.Empty,
                Transactions = Rlp.Encode(Array.Empty<Transaction>()).Bytes
            };
            
            blockRequest.BlockHash = blockRequest.ToBlock().CalculateHash();
            return blockRequest;
        }

        private Task<MergeTestBlockchain> CreateBlockChain() => MergeTestBlockchain.Build(new SingleReleaseSpecProvider(Berlin.Instance, 1));

        private IConsensusRpcModule CreateConsensusModule(MergeTestBlockchain chain)
        {
            return new ConsensusRpcModule(
                new AssembleBlockHandler(chain.BlockTree, (IEth2BlockProducer) chain.BlockProducer, chain.LogManager),
                new NewBlockHandler(chain.BlockTree, chain.BlockchainProcessor, chain.State, chain.LogManager),
                new SetHeadBlockHandler(chain.BlockTree, chain.LogManager),
                new FinaliseBlockHandler());
        }

        private class MergeTestBlockchain : TestBlockchain
        {
            private MergeTestBlockchain() { }
            
            protected override Task AddBlocksOnStart() => Task.CompletedTask;

            public override ILogManager LogManager { get; } = new NUnitLogManager();
            
            private BlockValidator BlockValidator { get; set; }
            
            private Signer Signer { get; set; }

            protected override ITestBlockProducer CreateTestBlockProducer(TxPoolTxSource txPoolTxSource, BlockchainProcessor chainProcessor, IStateProvider producerStateProvider, ISealer sealer)
            {
                return (ITestBlockProducer) new Eth2TestBlockProducerFactory().Create(
                    BlockTree,
                    DbProvider,
                    ReadOnlyTrieStore,
                    new RecoverSignatures(new EthereumEcdsa(SpecProvider.ChainId, LogManager), TxPool, SpecProvider, LogManager),
                    TxPool,
                    new BlockValidator(
                        new TxValidator(SpecProvider.ChainId),
                        new HeaderValidator(BlockTree, new Eth2SealEngine(Signer), SpecProvider, LogManager),
                        Always.Valid,
                        SpecProvider,
                        LogManager),
                    NoBlockRewards.Instance,
                    ReceiptStorage,
                    BlockProcessingQueue,
                    State,
                    SpecProvider,
                    Signer,
                    new MiningConfig(),
                    LogManager);
            }
            
            protected override BlockProcessor CreateBlockProcessor()
            {
                Signer = new(SpecProvider.ChainId, TestItem.PrivateKeyA, LogManager);
                HeaderValidator headerValidator = new HeaderValidator(BlockTree, new Eth2SealEngine(Signer), SpecProvider, LogManager);
                BlockValidator = new BlockValidator(
                    new TxValidator(SpecProvider.ChainId),
                    headerValidator,
                    new OmmersValidator(BlockTree, headerValidator, LogManager),
                    SpecProvider,
                    LogManager);
                    
                return new BlockProcessor(
                    SpecProvider,
                    BlockValidator,
                    NoBlockRewards.Instance,
                    TxProcessor,
                    State,
                    Storage,
                    TxPool,
                    ReceiptStorage,
                    NullWitnessCollector.Instance,
                    LogManager);
            }

            private async Task<MergeTestBlockchain> BuildInternal(ISpecProvider specProvider = null) => 
                (MergeTestBlockchain) await base.Build(specProvider);

            public static async Task<MergeTestBlockchain> Build(ISpecProvider specProvider = null) => 
                await new MergeTestBlockchain().BuildInternal(specProvider);
        }
    }
}
