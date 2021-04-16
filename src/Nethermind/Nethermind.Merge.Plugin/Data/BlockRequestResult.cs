﻿//  Copyright (c) 2021 Demerzel Solutions Limited
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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf.WellKnownTypes;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Extensions;
using Nethermind.Crypto;
using Nethermind.Int256;
using Nethermind.JsonRpc.Data;
using Nethermind.Serialization.Json;
using Nethermind.Serialization.Rlp;
using Nethermind.State.Proofs;
using Newtonsoft.Json;

namespace Nethermind.Merge.Plugin.Data
{
    public class BlockRequestResult
    {
        public BlockRequestResult(bool setDefaults = false)
        {
            if (setDefaults)
            {
                Difficulty = UInt256.One;
                Nonce = 0;
                ExtraData = Bytes.Empty;
                MixHash = Keccak.Zero;
                Uncles = Array.Empty<Keccak>();
            }
        }
        
        public BlockRequestResult(Block block)
        {
            BlockHash = block.Hash;
            ParentHash = block.ParentHash;
            Miner = block.Beneficiary;
            StateRoot = block.StateRoot;
            Number = block.Number;
            GasLimit = block.GasLimit;
            GasUsed = block.GasUsed;
            ReceiptsRoot = block.ReceiptsRoot;
            LogsBloom = block.Bloom;
            SetTransactions(block.Transactions);
            Difficulty = block.Difficulty;
            Nonce = block.Nonce;
            ExtraData = block.ExtraData;
            MixHash = block.MixHash;
            Uncles = block.Ommers.Select(o => o.Hash!);
            Timestamp = block.Timestamp;
        }

        public Block ToBlock()
        {
            BlockHeader header = new(ParentHash, Keccak.OfAnEmptySequenceRlp, Miner, Difficulty, Number, GasLimit, Timestamp, ExtraData)
            {
                Hash = BlockHash,
                ReceiptsRoot = ReceiptsRoot,
                StateRoot = StateRoot,
                MixHash = MixHash,
                Bloom = LogsBloom,
                GasUsed = GasUsed
            };
            Transaction[] transactions = Rlp.DecodeArray<Transaction>(new RlpStream(Transactions));
            header.TxRoot = new TxTrie(transactions).RootHash;
            return new Block(header, transactions, Array.Empty<BlockHeader>());
        }
        
        public UInt256 Difficulty { get; set; }
        public byte[] ExtraData { get; set; } = null!;
        public long GasLimit { get; set; }
        public long GasUsed { get; set; }
        
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public Keccak BlockHash { get; set; } = null!;

        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public Bloom LogsBloom { get; set; } = Bloom.Empty;

        public Address? Miner { get; set; }
        public Keccak MixHash { get; set; } = null!;

        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public ulong Nonce { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public long Number { get; set; }
        public Keccak ParentHash { get; set; } = null!;
        public Keccak ReceiptsRoot { get; set; } = null!;
        public Keccak StateRoot { get; set; } = null!;
        public byte[] Transactions { get; set; } = null!;
        public IEnumerable<Keccak>? Uncles { get; set; }
        public UInt256 Timestamp { get; set; }

        public override string ToString() => BlockHash == null ? $"{Number} null" : $"{Number} ({BlockHash})";

        public Keccak CalculateHash() => ToBlock().CalculateHash();
        public void SetTransactions(params Transaction[] transactions) => Transactions = Rlp.Encode(transactions).Bytes;

        public Transaction[] GetTransactions() => Rlp.DecodeArray<Transaction>(new RlpStream(Transactions));
    }
}
