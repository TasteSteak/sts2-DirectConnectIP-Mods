using System;
using System.Threading;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Platform;

namespace DirectConnectIP.Network
{
    public class DirectClientConnectionInitializer(string ip, ushort port, ulong? netId = null)
        : IClientConnectionInitializer
    {
        private readonly ulong _netId = netId ?? ModEntry.Config.LocalPlayerId;

        public async Task<NetErrorInfo?> Connect(
            INetClientGameService gameService,
            CancellationToken cancelToken = default)
        {
            if (gameService is not NetClientGameService concreteGameService)
                throw new InvalidOperationException("DirectClientConnectionInitializer requires NetClientGameService.");
            if (concreteGameService.IsConnected) throw new InvalidOperationException("NetClientGameService must not be connected when passed to DirectClientConnectionInitializer!");

            var client = new DirectClient(concreteGameService);
            concreteGameService.Initialize(client, PlatformType.None);
            return await client.ConnectToHost(_netId, ip, port, cancelToken);
        }

        public override string ToString()
        {
            return $"{nameof(DirectClientConnectionInitializer)} netId={_netId} host={ip}:{port}";
        }
    }
}
