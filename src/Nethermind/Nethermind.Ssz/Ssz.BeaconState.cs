//  Copyright (c) 2018 Demerzel Solutions Limited
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

using System;
using System.Collections;
using System.Linq;
using Nethermind.Core2;
using Nethermind.Core2.Containers;
using Nethermind.Core2.Types;

namespace Nethermind.Ssz
{
    public partial class Ssz
    {
        public static void Encode(Span<byte> span, BeaconState? container)
        {
            if (container is null)
            {
                return;
            }
            
            if (span.Length != Ssz.BeaconStateLength(container))
            {
                ThrowTargetLength<BeaconState>(span.Length, Ssz.BeaconStateLength(container));
            }

            int offset = 0;
            int dynamicOffset = Ssz.BeaconStateDynamicOffset;

            Encode(span.Slice(offset, sizeof(ulong)), container.GenesisTime);
            offset += sizeof(ulong);
            Encode(span, container.Slot, ref offset);
            Encode(span, container.Fork, ref offset);
            Encode(span.Slice(offset, Ssz.BeaconBlockHeaderLength), container.LatestBlockHeader);
            offset += Ssz.BeaconBlockHeaderLength;
            Encode(span.Slice(offset, Ssz.SlotsPerHistoricalRoot * Ssz.Hash32Length), container.BlockRoots);
            offset += Ssz.SlotsPerHistoricalRoot * Ssz.Hash32Length;
            Encode(span.Slice(offset, Ssz.SlotsPerHistoricalRoot * Ssz.Hash32Length), container.StateRoots);
            offset += Ssz.SlotsPerHistoricalRoot * Ssz.Hash32Length;
            int length1 = container.HistoricalRoots.Count * Ssz.Hash32Length;
            Encode(span.Slice(offset, VarOffsetSize), dynamicOffset);
            Encode(span.Slice(dynamicOffset, length1), container.HistoricalRoots);
            dynamicOffset += length1;
            offset += VarOffsetSize;
            Encode(span, container.Eth1Data, ref offset);
            int length2 = container.Eth1DataVotes.Count * Ssz.Eth1DataLength;
            Encode(span.Slice(offset, VarOffsetSize), dynamicOffset);
            Encode(span.Slice(dynamicOffset, length2), container.Eth1DataVotes.ToArray());
            dynamicOffset += length2;
            offset += VarOffsetSize;
            Encode(span.Slice(offset, sizeof(ulong)), container.Eth1DepositIndex);
            offset += sizeof(ulong);
            int length3 = container.Validators.Count * Ssz.ValidatorLength;
            Encode(span.Slice(offset, VarOffsetSize), dynamicOffset);
            Encode(span.Slice(dynamicOffset, length3), container.Validators.ToArray());
            dynamicOffset += length3;
            offset += VarOffsetSize;
            int length4 = container.Balances.Count * Ssz.GweiLength;
            Encode(span.Slice(offset, VarOffsetSize), dynamicOffset);
            Encode(span.Slice(dynamicOffset, length4), container.Balances.ToArray());
            dynamicOffset += length4;
            offset += VarOffsetSize;
            Encode(span.Slice(offset, Ssz.EpochsPerHistoricalVector * Ssz.Hash32Length), container.RandaoMixes);
            offset += Ssz.EpochsPerHistoricalVector * Ssz.Hash32Length;
            Encode(span.Slice(offset, Ssz.EpochsPerSlashingsVector * Ssz.GweiLength), container.Slashings.ToArray());
            offset += Ssz.EpochsPerSlashingsVector * Ssz.GweiLength;

            int length5 = container.PreviousEpochAttestations.Count * VarOffsetSize;
            for (int i = 0; i < container.PreviousEpochAttestations.Count; i++)
            {
                length5 += Ssz.PendingAttestationLength(container.PreviousEpochAttestations[i]);
            }

            Encode(span.Slice(offset, VarOffsetSize), dynamicOffset);
            Encode(span.Slice(dynamicOffset, length5), container.PreviousEpochAttestations.ToArray());
            dynamicOffset += length5;
            offset += VarOffsetSize;

            int length6 = container.CurrentEpochAttestations.Count * VarOffsetSize;
            for (int i = 0; i < container.CurrentEpochAttestations.Count; i++)
            {
                length6 += Ssz.PendingAttestationLength(container.CurrentEpochAttestations[i]);
            }

            Encode(span.Slice(offset, VarOffsetSize), dynamicOffset);
            Encode(span.Slice(dynamicOffset, length6), container.CurrentEpochAttestations.ToArray());
            dynamicOffset += length6;
            offset += VarOffsetSize;
            Encode(span, container.JustificationBits, ref offset);
            Encode(span, container.PreviousJustifiedCheckpoint, ref offset);
            Encode(span, container.CurrentJustifiedCheckpoint, ref offset);
            Encode(span, container.FinalizedCheckpoint, ref offset);
        }

        public static BeaconState DecodeBeaconState(Span<byte> span)
        {
            int offset = 0;

            var genesisTime = DecodeULong(span, ref offset);
            var slot = DecodeSlot(span, ref offset);
            var fork = DecodeFork(span, ref offset);
            var latestBlockHeader = DecodeBeaconBlockHeader(span, ref offset);

            var blockRoots = DecodeHashes(span.Slice(offset, Ssz.SlotsPerHistoricalRoot * Ssz.Hash32Length)).ToArray();
            offset += Ssz.SlotsPerHistoricalRoot * Ssz.Hash32Length;
            var stateRoots = DecodeHashes(span.Slice(offset, Ssz.SlotsPerHistoricalRoot * Ssz.Hash32Length)).ToArray();
            offset += Ssz.SlotsPerHistoricalRoot * Ssz.Hash32Length;

            DecodeDynamicOffset(span, ref offset, out int dynamicOffset1);
            var eth1Data = DecodeEth1Data(span, ref offset);
            DecodeDynamicOffset(span, ref offset, out int dynamicOffset2);
            var eth1DepositIndex = DecodeULong(span, ref offset);
            DecodeDynamicOffset(span, ref offset, out int dynamicOffset3);
            DecodeDynamicOffset(span, ref offset, out int dynamicOffset4);
            var randaoMixes = DecodeHashes(span.Slice(offset, Ssz.EpochsPerHistoricalVector * Ssz.Hash32Length));
            offset += Ssz.EpochsPerHistoricalVector * Ssz.Hash32Length;
            var slashings = DecodeGweis(span.Slice(offset, Ssz.EpochsPerSlashingsVector * Ssz.GweiLength));
            offset += Ssz.EpochsPerSlashingsVector * Ssz.GweiLength;
            DecodeDynamicOffset(span, ref offset, out int dynamicOffset5);
            DecodeDynamicOffset(span, ref offset, out int dynamicOffset6);
            
            var justificationBitsByteLength = (Ssz.JustificationBitsLength + 7) / 8;
            BitArray justificationBits = DecodeBitvector(span.Slice(offset, justificationBitsByteLength), Ssz.JustificationBitsLength);
            offset += justificationBitsByteLength;
            
            var previousJustifiedCheckpoint = DecodeCheckpoint(span, ref offset);
            var currentJustifiedCheckpoint = DecodeCheckpoint(span, ref offset);
            var finalizedCheckpoint = DecodeCheckpoint(span, ref offset);

            var historicalRoots = DecodeHashes(span.Slice(dynamicOffset1, dynamicOffset2 - dynamicOffset1));
            var eth1DataVotes = DecodeEth1Datas(span.Slice(dynamicOffset2, dynamicOffset3 - dynamicOffset2));
            var validators = DecodeValidators(span.Slice(dynamicOffset3, dynamicOffset4 - dynamicOffset3));
            var balances = DecodeGweis(span.Slice(dynamicOffset4, dynamicOffset5 - dynamicOffset4));
            var previousEpochAttestations = DecodePendingAttestations(span.Slice(dynamicOffset5, dynamicOffset6 - dynamicOffset5));
            var currentEpochAttestations = DecodePendingAttestations(span.Slice(dynamicOffset6, span.Length - dynamicOffset6));

            BeaconState beaconState = new BeaconState(
                genesisTime,
                slot,
                fork,
                latestBlockHeader,
                blockRoots,
                stateRoots,
                historicalRoots,
                eth1Data,
                eth1DataVotes,
                eth1DepositIndex,
                validators,
                balances,
                randaoMixes,
                slashings,
                previousEpochAttestations,
                currentEpochAttestations,
                justificationBits,
                previousJustifiedCheckpoint,
                currentJustifiedCheckpoint,
                finalizedCheckpoint);

            return beaconState;
        }
    }
}