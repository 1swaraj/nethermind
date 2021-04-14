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

using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using Nethermind.Blockchain;
using Nethermind.Blockchain.Comparers;
using Nethermind.Blockchain.Receipts;
using Nethermind.Blockchain.Spec;
using Nethermind.Blockchain.Synchronization;
using Nethermind.Blockchain.Validators;
using Nethermind.Config;
using Nethermind.Consensus;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Test;
using Nethermind.Specs;
using Nethermind.Core.Test.Builders;
using Nethermind.Crypto;
using Nethermind.Db;
using Nethermind.Int256;
using Nethermind.JsonRpc.Modules.Parity;
using Nethermind.Logging;
using Nethermind.State;
using Nethermind.State.Repositories;
using Nethermind.Db.Blooms;
using Nethermind.KeyStore;
using Nethermind.Network;
using Nethermind.Network.P2P;
using Nethermind.Specs.Forks;
using Nethermind.Stats.Model;
using Nethermind.Trie.Pruning;
using Nethermind.TxPool;
using Nethermind.TxPool.Storages;
using NSubstitute;
using NUnit.Framework;
using BlockTree = Nethermind.Blockchain.BlockTree;

namespace Nethermind.JsonRpc.Test.Modules
{
    [Parallelizable(ParallelScope.Self)]
    [TestFixture]
    public class ParityModuleTests
    {
        private IParityRpcModule _parityRpcModule;
        private Signer _signerStore;

        [SetUp]
        public void Initialize()
        {
            var logger = LimboLogs.Instance;
            var specProvider = MainnetSpecProvider.Instance;
            var ethereumEcdsa = new EthereumEcdsa(specProvider.ChainId, logger);
            var txStorage = new InMemoryTxStorage();
            
            Peer peerA = SetUpPeerA();      //standard case
            Peer peerB = SetUpPeerB();      //Session is null
            Peer peerC = SetUpPeerC();      //Node is null, Caps are empty
            IPeerManager peerManager = Substitute.For<IPeerManager>();
            peerManager.ActivePeers.Returns(new List<Peer> {peerA, peerB, peerC});
            peerManager.ConnectedPeers.Returns(new List<Peer> {peerA, peerB, peerA, peerC, peerB});
            peerManager.MaxActivePeers.Returns(15);

            StateProvider stateProvider = new StateProvider(new TrieStore(new MemDb(), LimboLogs.Instance), new MemDb(), LimboLogs.Instance);
           
            IDb blockDb = new MemDb();
            IDb headerDb = new MemDb();
            IDb blockInfoDb = new MemDb();
            IBlockTree blockTree = new BlockTree(blockDb, headerDb, blockInfoDb, new ChainLevelInfoRepository(blockInfoDb), specProvider, NullBloomStorage.Instance, LimboLogs.Instance);

            ITransactionComparerProvider transactionComparerProvider =
                new TransactionComparerProvider(specProvider, blockTree);
            var txPool = new TxPool.TxPool(txStorage, ethereumEcdsa, new FixedBlockChainHeadSpecProvider(specProvider), new TxPoolConfig(),
                stateProvider, new TxValidator(specProvider.ChainId), LimboLogs.Instance, transactionComparerProvider.GetDefaultComparer());
            
            new OnChainTxWatcher(blockTree, txPool, specProvider, LimboLogs.Instance);
            
            IReceiptStorage receiptStorage = new InMemoryReceiptStorage();

            _signerStore = new Signer(specProvider.ChainId, TestItem.PrivateKeyB, logger);
            _parityRpcModule = new ParityRpcModule(ethereumEcdsa, txPool, blockTree, receiptStorage, new Enode(TestItem.PublicKeyA, IPAddress.Loopback, 8545), 
                _signerStore, new MemKeyStore(new[] {TestItem.PrivateKeyA}),  logger, peerManager);

            var blockNumber = 2;
            var pendingTransaction = Build.A.Transaction.Signed(ethereumEcdsa, TestItem.PrivateKeyD, false)
                .WithSenderAddress(Address.FromNumber((UInt256)blockNumber)).TestObject;
            pendingTransaction.Signature.V = 37;
            stateProvider.CreateAccount(pendingTransaction.SenderAddress, UInt256.UInt128MaxValue);
            txPool.AddTransaction(pendingTransaction, TxHandlingOptions.None);
            
            blockNumber = 1;
            var transaction = Build.A.Transaction.Signed(ethereumEcdsa, TestItem.PrivateKeyD, false)
                .WithSenderAddress(Address.FromNumber((UInt256)blockNumber))
                .WithNonce(100).TestObject;
            transaction.Signature.V = 37;
            stateProvider.CreateAccount(transaction.SenderAddress, UInt256.UInt128MaxValue);
            txPool.AddTransaction(transaction, TxHandlingOptions.None);

            
            Block genesis = Build.A.Block.Genesis
                .WithStateRoot(new Keccak("0x1ef7300d8961797263939a3d29bbba4ccf1702fabf02d8ad7a20b454edb6fd2f"))
                .TestObject;
            
            blockTree.SuggestBlock(genesis);
            blockTree.UpdateMainChain(new[] {genesis}, true);

            Block previousBlock = genesis;
            Block block = Build.A.Block.WithNumber(blockNumber).WithParent(previousBlock)
                    .WithStateRoot(new Keccak("0x1ef7300d8961797263939a3d29bbba4ccf1702fabf02d8ad7a20b454edb6fd2f"))
                    .WithTransactions(transaction)
                    .TestObject;
                
            blockTree.SuggestBlock(block);
            blockTree.UpdateMainChain(new[] {block}, true);

            var logEntries = new[] {Build.A.LogEntry.TestObject};
            receiptStorage.Insert(block, new TxReceipt()
            {
                Bloom = new Bloom(logEntries),
                Index = 1,
                Recipient = TestItem.AddressA,
                Sender = TestItem.AddressB,
                BlockHash = TestItem.KeccakA,
                BlockNumber = 1,
                ContractAddress = TestItem.AddressC,
                GasUsed = 1000,
                TxHash = transaction.Hash,
                StatusCode = 0,
                GasUsedTotal = 2000,
                Logs = logEntries
            });
        }
        
        private static Peer SetUpPeerA()
        {
            Node node = new Node("127.0.0.1", 30303, true);
            node.ClientId = "Geth/v1.9.21-stable/linux-amd64/go1.15.2";
            
            Peer peer = new Peer(node);
            peer.InSession = null;
            peer.OutSession = Substitute.For<ISession>();
            peer.OutSession.RemoteNodeId.Returns(TestItem.PublicKeyA);
            
            var protocolHandler = Substitute.For<IProtocolHandler, ISyncPeer>();
            peer.OutSession.TryGetProtocolHandler(Protocol.Eth, out Arg.Any<IProtocolHandler>()).Returns(x =>
            {
                x[1] = protocolHandler;
                return true;
            });

            byte version = 65;
            protocolHandler.ProtocolVersion.Returns(version);
            if (protocolHandler is ISyncPeer syncPeer)
            {
                UInt256 difficulty = 0x5ea4ed;
                syncPeer.TotalDifficulty.Returns(difficulty);
                syncPeer.HeadHash.Returns(TestItem.KeccakA);
            }

            var p2PProtocolHandler = Substitute.For<IProtocolHandler, IP2PProtocolHandler>();
            peer.OutSession.TryGetProtocolHandler(Protocol.P2P, out Arg.Any<IProtocolHandler>()).Returns(x =>
            {
                x[1] = p2PProtocolHandler;
                return true;
            });
            
             if (p2PProtocolHandler is IP2PProtocolHandler p2PHandler)
             {
                 p2PHandler.AgreedCapabilities.Returns(new List<Capability>{new Capability("eth", 65), new Capability("eth", 64)});
             }
            
            return peer;
        }
        
        private static Peer SetUpPeerB()
        {
            Node node = new Node("95.217.106.25", 22222, true);
            node.ClientId = "Geth/v1.9.26-unstable/linux-amd64/go1.15.6";

            Peer peer = new Peer(node);
            peer.InSession = null;
            peer.OutSession = null;
            
            return peer;
        }
        
        private static Peer SetUpPeerC()
        {
            Peer peer = new Peer(null);
            peer.InSession = Substitute.For<ISession>();
            peer.InSession.RemoteNodeId.Returns(TestItem.PublicKeyB);
            
            var p2PProtocolHandler = Substitute.For<IProtocolHandler, IP2PProtocolHandler>();
            peer.InSession.TryGetProtocolHandler(Protocol.P2P, out Arg.Any<IProtocolHandler>()).Returns(x =>
            {
                x[1] = p2PProtocolHandler;
                return true;
            });
            
            if (p2PProtocolHandler is IP2PProtocolHandler p2PHandler)
            {
                p2PHandler.AgreedCapabilities.Returns(new List<Capability>{});
            }
            
            return peer;
        }

        [Test]
        public async Task parity_pendingTransactions()
        {
            await Task.Delay(100);
            string serialized = RpcTest.TestSerializedRequest(_parityRpcModule, "parity_pendingTransactions");
            var expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":[{\"hash\":\"0xd4720d1b81c70ed4478553a213a83bd2bf6988291677f5d05c6aae0b287f947e\",\"nonce\":\"0x0\",\"blockHash\":null,\"blockNumber\":null,\"transactionIndex\":null,\"from\":\"0x0000000000000000000000000000000000000002\",\"to\":\"0x0000000000000000000000000000000000000000\",\"value\":\"0x1\",\"gasPrice\":\"0x1\",\"gas\":\"0x5208\",\"input\":\"0x\",\"raw\":\"0xf85f8001825208940000000000000000000000000000000000000000018025a0ef2effb79771cbe42fc7f9cc79440b2a334eedad6e528ea45c2040789def4803a0515bdfe298808be2e07879faaeacd0ad17f3b13305b9f971647bbd5d5b584642\",\"creates\":null,\"publicKey\":\"0x15a1cc027cfd2b970c8aa2b3b22dfad04d29171109f6502d5fb5bde18afe86dddd44b9f8d561577527f096860ee03f571cc7f481ea9a14cb48cc7c20c964373a\",\"chainId\":1,\"condition\":null,\"r\":\"0xef2effb79771cbe42fc7f9cc79440b2a334eedad6e528ea45c2040789def4803\",\"s\":\"0x515bdfe298808be2e07879faaeacd0ad17f3b13305b9f971647bbd5d5b584642\",\"v\":\"0x25\",\"standardV\":\"0x0\"}],\"id\":67}";
            Assert.AreEqual(expectedResult, serialized);
        }
        
        [Test]
        public void parity_getBlockReceipts()
        {
            string serialized = RpcTest.TestSerializedRequest(_parityRpcModule, "parity_getBlockReceipts", "latest");
            var expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":[{\"transactionHash\":\"0x026217c3c4eb1f0e9e899553759b6e909b965a789c6136d256674718617c8142\",\"transactionIndex\":\"0x1\",\"blockHash\":\"0x7adb9df3043091c79726047c0c46a9d59f65bc8e988b96d5e60c60b07befc3b7\",\"blockNumber\":\"0x1\",\"cumulativeGasUsed\":\"0x7d0\",\"gasUsed\":\"0x3e8\",\"from\":\"0x942921b14f1b1c385cd7e0cc2ef7abe5598c8358\",\"to\":\"0xb7705ae4c6f81b66cdb323c65f4e8133690fc099\",\"contractAddress\":\"0x76e68a8696537e4141926f3e528733af9e237d69\",\"logs\":[{\"removed\":false,\"logIndex\":\"0x0\",\"transactionIndex\":\"0x1\",\"transactionHash\":\"0x026217c3c4eb1f0e9e899553759b6e909b965a789c6136d256674718617c8142\",\"blockHash\":\"0x7adb9df3043091c79726047c0c46a9d59f65bc8e988b96d5e60c60b07befc3b7\",\"blockNumber\":\"0x1\",\"address\":\"0x0000000000000000000000000000000000000000\",\"data\":\"0x\",\"topics\":[\"0x0000000000000000000000000000000000000000000000000000000000000000\"]}],\"logsBloom\":\"0x00000000000000000080000000000000000000000000000000000000000000000000000000000000000000000000000200000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000020000000000000000000800000000000000000000000000000000000000000000000000000000000000000100000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000020000000000000000000000000000000000000000000000000000000000000000000\",\"status\":\"0x0\",\"type\":\"0x00\"}],\"id\":67}";
            Assert.AreEqual(expectedResult, serialized);
        }
        
        [Test]
        public void parity_enode()
        {
            string serialized = RpcTest.TestSerializedRequest(_parityRpcModule, "parity_enode");
            var expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":\"enode://a49ac7010c2e0a444dfeeabadbafa4856ba4a2d732acb86d20c577b3b365fdaeb0a70ce47f890cf2f9fca562a7ed784f76eb870a2c75c0f2ab476a70ccb67e92@127.0.0.1:8545\",\"id\":67}";
            Assert.AreEqual(expectedResult, serialized);
        }
        
        [Test]
        public void parity_setEngineSigner()
        {
            string serialized = RpcTest.TestSerializedRequest(_parityRpcModule, "parity_setEngineSigner", TestItem.AddressA.ToString(), "password");
            var expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":true,\"id\":67}";
            Assert.AreEqual(expectedResult, serialized);
            _signerStore.Address.Should().Be(TestItem.AddressA);
            _signerStore.CanSign.Should().BeTrue();
        }
        
        [Test]
        public void parity_setEngineSignerSecret()
        {
            string serialized = RpcTest.TestSerializedRequest(_parityRpcModule, "parity_setEngineSignerSecret", TestItem.PrivateKeyA.ToString());
            var expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":true,\"id\":67}";
            Assert.AreEqual(expectedResult, serialized);
            _signerStore.Address.Should().Be(TestItem.AddressA);
            _signerStore.CanSign.Should().BeTrue();
        }
        
        [Test]
        public void parity_clearEngineSigner()
        {
            RpcTest.TestSerializedRequest(_parityRpcModule, "parity_setEngineSigner", TestItem.AddressA.ToString(), "password");
            string serialized = RpcTest.TestSerializedRequest(_parityRpcModule, "parity_clearEngineSigner");
            var expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":true,\"id\":67}";
            serialized.Should().Be(expectedResult);
            _signerStore.Address.Should().Be(Address.Zero);
            _signerStore.CanSign.Should().BeFalse();
        }

        [Test]
        public void parity_netPeers_standard_case()
        {
            string serialized = RpcTest.TestSerializedRequest(_parityRpcModule, "parity_netPeers");
            var expectedResult = string.Concat("{\"jsonrpc\":\"2.0\",\"result\":{\"active\":3,\"connected\":5,\"max\":15,\"peers\":[{\"id\":\"", TestItem.PublicKeyA, "\",\"name\":\"Geth/v1.9.21-stable/linux-amd64/go1.15.2\",\"caps\":[\"eth/65\",\"eth/64\"],\"network\":{\"localAddress\":\"127.0.0.1\",\"remoteAddress\":\"Handshake\"},\"protocols\":{\"eth\":{\"version\":65,\"difficulty\":\"0x5ea4ed\",\"head\":\"", TestItem.KeccakA, "\"}}},{\"name\":\"Geth/v1.9.26-unstable/linux-amd64/go1.15.6\",\"caps\":[],\"network\":{\"localAddress\":\"95.217.106.25\"},\"protocols\":{\"eth\":{\"version\":0,\"difficulty\":\"0x0\"}}},{\"id\":\"", TestItem.PublicKeyB, "\",\"caps\":[],\"network\":{\"remoteAddress\":\"Handshake\"},\"protocols\":{\"eth\":{\"version\":0,\"difficulty\":\"0x0\"}}}]},\"id\":67}");
            Assert.AreEqual(expectedResult, serialized);
        }

        [Test]
        public void parity_netPeers_empty_ActivePeers()
        {
            var logger = LimboLogs.Instance;
            var specProvider = MainnetSpecProvider.Instance;
            var ethereumEcdsa = new EthereumEcdsa(specProvider.ChainId, logger);
            InMemoryTxStorage txStorage = new InMemoryTxStorage();
            IDb blockDb = new MemDb();
            IDb headerDb = new MemDb();
            IDb blockInfoDb = new MemDb();
            IBlockTree blockTree = new BlockTree(blockDb, headerDb, blockInfoDb, new ChainLevelInfoRepository(blockInfoDb), specProvider, NullBloomStorage.Instance, LimboLogs.Instance);

            ITransactionComparerProvider transactionComparerProvider =
                new TransactionComparerProvider(specProvider, blockTree);
            var txPool = new TxPool.TxPool(txStorage, ethereumEcdsa, new ChainHeadSpecProvider(specProvider, blockTree), new TxPoolConfig(),
                new StateProvider(new TrieStore(new MemDb(), LimboLogs.Instance), new MemDb(), LimboLogs.Instance),  new TxValidator(specProvider.ChainId), LimboLogs.Instance, transactionComparerProvider.GetDefaultComparer());

            new OnChainTxWatcher(blockTree, txPool, specProvider, LimboLogs.Instance);
            IReceiptStorage receiptStorage = new InMemoryReceiptStorage();

            IPeerManager peerManager = Substitute.For<IPeerManager>();
            peerManager.ActivePeers.Returns(new List<Peer>{});
            peerManager.ConnectedPeers.Returns(new List<Peer> {new Peer(new Node("111.1.1.1", 11111, true))});
            
            IParityRpcModule parityRpcModule = new ParityRpcModule(ethereumEcdsa, txPool, blockTree, receiptStorage, new Enode(TestItem.PublicKeyA, IPAddress.Loopback, 8545), 
                _signerStore, new MemKeyStore(new[] {TestItem.PrivateKeyA}),  logger, peerManager);
            
            string serialized = RpcTest.TestSerializedRequest(parityRpcModule, "parity_netPeers");
            var expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":{\"active\":0,\"connected\":1,\"max\":0,\"peers\":[]},\"id\":67}";
            Assert.AreEqual(expectedResult, serialized);
        }
        
        [Test]
        public void parity_netPeers_null_ActivePeers()
        {
            var logger = LimboLogs.Instance;
            var specProvider = MainnetSpecProvider.Instance;
            var ethereumEcdsa = new EthereumEcdsa(specProvider.ChainId, logger);
            var txStorage = new InMemoryTxStorage();
            
            IDb blockDb = new MemDb();
            IDb headerDb = new MemDb();
            IDb blockInfoDb = new MemDb();
            IBlockTree blockTree = new BlockTree(blockDb, headerDb, blockInfoDb, new ChainLevelInfoRepository(blockInfoDb), specProvider, NullBloomStorage.Instance, LimboLogs.Instance);
            ITransactionComparerProvider transactionComparerProvider =
                new TransactionComparerProvider(specProvider, blockTree);
            var txPool = new TxPool.TxPool(txStorage, ethereumEcdsa, new ChainHeadSpecProvider(specProvider, blockTree), new TxPoolConfig(),
                new StateProvider(new TrieStore(new MemDb(), LimboLogs.Instance), new MemDb(), LimboLogs.Instance), new TxValidator(specProvider.ChainId), LimboLogs.Instance, transactionComparerProvider.GetDefaultComparer());

            new OnChainTxWatcher(blockTree, txPool, specProvider, LimboLogs.Instance);
            IReceiptStorage receiptStorage = new InMemoryReceiptStorage();

            IPeerManager peerManager = Substitute.For<IPeerManager>();
            
            IParityRpcModule parityRpcModule = new ParityRpcModule(ethereumEcdsa, txPool, blockTree, receiptStorage, new Enode(TestItem.PublicKeyA, IPAddress.Loopback, 8545), 
                _signerStore, new MemKeyStore(new[] {TestItem.PrivateKeyA}),  logger, peerManager);
            string serialized = RpcTest.TestSerializedRequest(parityRpcModule, "parity_netPeers");
            var expectedResult = "{\"jsonrpc\":\"2.0\",\"result\":{\"active\":0,\"connected\":0,\"max\":0,\"peers\":[]},\"id\":67}";
            Assert.AreEqual(expectedResult, serialized);
        }
    }
}
