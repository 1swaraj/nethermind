﻿//  Copyright (c) 2020 Demerzel Solutions Limited
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


using System.Threading.Tasks;
using Nethermind.Api;
using Nethermind.Api.Extensions;
using Nethermind.JsonRpc.Modules;
using Nethermind.PubSub;
using Nethermind.TxPool;

namespace Nethermind.Analytics
{
    public class AnalyticsPlugin : INethermindPlugin
    {
        private INethermindApi _api;
        private IAnalyticsConfig _analyticsConfig;

        public void Dispose()
        {
        }

        public string Name => "Analytics";

        public string Description => "Various Analytics Extensions";

        public string Author => "Nethermind";

        public Task Init(INethermindApi api)
        {
            _api = api;
            _analyticsConfig = _api.Config<IAnalyticsConfig>();
            IInitConfig initConfig = _api.Config<IInitConfig>();
            if (initConfig.WebSocketsEnabled &&
                (_analyticsConfig.PluginsEnabled ||
                 _analyticsConfig.StreamBlocks ||
                 _analyticsConfig.StreamTransactions))
            {
                AnalyticsWebSocketsModule webSocketsModule = new AnalyticsWebSocketsModule(_api.EthereumJsonSerializer);
                _api.WebSocketsManager!.AddModule(webSocketsModule, true);
                _api.Publishers.Add(webSocketsModule);

                _api.TxPool.NewDiscovered += TxPoolOnNewDiscovered;
            }

            return Task.CompletedTask;
        }

        private void TxPoolOnNewDiscovered(object sender, TxEventArgs e)
        {
            if (_analyticsConfig.StreamTransactions)
            {
                foreach (IPublisher publisher in _api.Publishers)
                {
                    // TODO: probably need to serialize first
                    publisher.PublishAsync(e.Transaction);
                }
            }
        }

        public Task InitNetworkProtocol()
        {
            return Task.CompletedTask;
        }

        public Task InitRpcModules()
        {
            AnalyticsModule analyticsModule = new AnalyticsModule(_api.BlockTree, _api.StateReader, _api.LogManager);
            _api.RpcModuleProvider.Register(new SingletonModulePool<IAnalyticsModule>(analyticsModule));
            return Task.CompletedTask;
        }
    }
}