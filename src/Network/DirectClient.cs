#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DirectConnectIP.Helpers;
using DirectConnectIP.Network.Packets;
using DirectConnectIP.Patches.Network;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Multiplayer.Transport.ENet;

namespace DirectConnectIP.Network
{
    public class DirectClient(INetClientHandler handler) : NetClient(handler)
    {
        private readonly MegaCrit.Sts2.Core.Logging.Logger _logger = new (nameof(DirectClient), LogType.Network);
        private ENetConnection? _connection;
        private ENetPacketPeer? _peer;
        private bool _isConnected;
        private ulong _netId;
        private ulong _hostNetId;

        public override bool IsConnected => _isConnected;
        public override ulong NetId => _netId;
        public override ulong HostNetId => _hostNetId;

        public async Task<NetErrorInfo?> ConnectToHost(ulong myNetId, string ip, ushort port, CancellationToken cancelToken = default)
        {
            _netId = myNetId;
            _connection = new ENetConnection();
            _connection.CreateHost();

            _peer = _connection.ConnectToHost(ip, port);
            _logger.Info($"Connecting to {ip}:{port} with ID {myNetId}");

            var timeout = 0;
            while (true)
            {
                if (_connection == null) return new NetErrorInfo(NetError.CancelledJoin, false);

                if (_connection.TryService(out var data))
                {
                    if (data!.Value.type == ENetConnection.EventType.Connect) break;
                    if (data.Value.type == ENetConnection.EventType.Disconnect) return new NetErrorInfo(NetError.UnknownNetworkError, false);
                }

                try { await Task.Delay(100, cancelToken); }
                catch (OperationCanceledException)
                {
                    DisconnectFromHost(NetError.CancelledJoin);
                    return null;
                }

                timeout += 100;
                if (timeout <= 10000) continue;
                _peer?.Reset();
                return new NetErrorInfo(NetError.Timeout, false);
            }

            var handshakeReq = ENetPacket.FromHandshakeRequest(new ENetHandshakeRequest { netId = myNetId });
            _peer?.Send(0, handshakeReq.AllBytes, 1);

            var bufferedPackets = new List<ENetServiceData>();
            var (response, error) = await WaitForHandshakeResponse(bufferedPackets, cancelToken);
            if (error != null) return error;
            if (response == null) return new NetErrorInfo(NetError.InternalError, false);

            if (response.Value.status != ENetHandshakeStatus.Success)
            {
                _peer?.PeerDisconnect();
                return new NetErrorInfo(NetError.Kicked, false);
            }

            _hostNetId = response.Value.netId;
            _isConnected = true;
            _handler.OnConnectedToHost();
            
            var fullAddr = $"{ip}:{port}";
            ModEntry.Config.AddSuccessfulServer(fullAddr);
            PlayerNameRegistry.RemoteNames.Clear();
            
            SendModPacket(new SyncClientNamePacket(ModEntry.Config.LocalPlayerName));

            foreach (var data in bufferedPackets)
            {
                HandleMessageReceived(data);
            }

            _logger.Info($"Connected to host {_hostNetId}");
            return null;
        }

        private async Task<(ENetHandshakeResponse? response, NetErrorInfo? error)> WaitForHandshakeResponse(
            List<ENetServiceData> bufferedPackets, CancellationToken cancelToken)
        {
            var timeout = 0;
            while (true)
            {
                try { await Task.Delay(100, cancelToken); }
                catch (OperationCanceledException)
                {
                    DisconnectFromHost(NetError.CancelledJoin);
                    return (null, null);
                }

                if (_connection == null) return (null, new NetErrorInfo(NetError.UnknownNetworkError, false));

                if (_connection.TryService(out var data))
                {
                    if (data!.Value.type == ENetConnection.EventType.Receive)
                    {
                        var packet = new ENetPacket(data.Value.packetData);
                        if (packet.PacketType != ENetPacketType.HandshakeResponse)
                        {
                            if (packet.PacketType == ENetPacketType.ApplicationMessage)
                            {
                                bufferedPackets.Add(data.Value);
                            }
                        }
                        else
                        {
                            var resp = packet.AsHandshakeResponse();
                            if (resp.status != ENetHandshakeStatus.IdCollision)
                            {
                                if (resp.status == ENetHandshakeStatus.Success) return (resp, null);
                                _logger.Error($"Handshake failed with unknown status: {resp.status}");
                                return (null, new NetErrorInfo(NetError.UnknownNetworkError, false));
                            }

                            _logger.Error($"Handshake rejected: ID Collision ({resp.netId})");
                            return (null, new NetErrorInfo(NetError.Kicked, false));
                        }
                    }
                    else if (data.Value.type == ENetConnection.EventType.Disconnect)
                        return (null, new NetErrorInfo(NetError.UnknownNetworkError, false));
                }

                timeout += 100;
                if (timeout > 10000) return (null, new NetErrorInfo(NetError.Timeout, false));
            }
        }

        public override void Update()
        {
            if (!_isConnected || _connection == null) return;

            if (_peer != null && !_peer.IsActive())
            {
                TriggerDisconnect(new NetErrorInfo(NetError.UnknownNetworkError, false));
                return;
            }

            try
            {
                while (_connection != null && _connection.TryService(out var data))
                {
                    if (data!.Value.type == ENetConnection.EventType.Receive)
                    {
                        HandleMessageReceived(data.Value);
                    }
                    else if (data.Value.type == ENetConnection.EventType.Disconnect)
                    {
                        TriggerDisconnect(new NetErrorInfo(NetError.UnknownNetworkError, false));
                        break;
                    }
                }
            }
            catch (ObjectDisposedException) 
            { 
                _isConnected = false; 
                _connection = null; 
            }
            catch (Exception ex)
            {
                _logger.Error($"Error in Update: {ex.Message}\n{ex.StackTrace}");
                _isConnected = false;
                _connection = null;
            }
        }

        private void TriggerDisconnect(NetErrorInfo errorInfo)
        {
            if (!_isConnected) return;
            
            _isConnected = false;
            PlayerNameRegistry.RemoteNames.Clear();
            
            _connection?.Flush();
            _connection?.Destroy();
            _connection = null;
            
            _handler.OnDisconnectedFromHost(_hostNetId, errorInfo);
        }

        private void HandleMessageReceived(ENetServiceData data)
        {
            if (!_isConnected) return;

            try
            {
                var packet = new ENetPacket(data.packetData);
                if (packet.PacketType != ENetPacketType.ApplicationMessage)
                {
                    if (packet.PacketType != ENetPacketType.Disconnection) return;
                    var dis = packet.AsDisconnection();
                    TriggerDisconnect(new NetErrorInfo(dis.reason, false));
                }
                else
                {
                    var rawData = packet.AsAppMessage();
                    if (data.channel == ModPacketRouter.Channel)
                    {
                        if (!ModPacketRouter.IsModPacket(rawData)) return;
                        
                        var modPacket = ModPacketRouter.Deserialize(rawData);
                        if (modPacket != null) HandleModPacket(modPacket);
                        return;
                    }
                    
                    _handler.OnPacketReceived(HostNetId, rawData, data.mode, data.channel);
                }
            }
            catch (Exception ex) 
            {
                ModPacketRouter.LogNetworkCrash(ex, data.packetData, "客户端处理网络接收包时发生未捕获异常", data.channel);
                DisconnectFromHost(NetError.UnknownNetworkError, now: true);
            }
        }

        private void SendModPacket(IModPacket packet)
        {
            if (!_isConnected || _peer == null) return;
            var packetData = ModPacketRouter.Serialize(packet);
            var enetPacket = ENetPacket.FromAppMessage(packetData, packetData.Length);
            _peer.Send(ModPacketRouter.Channel, enetPacket.AllBytes, ENetUtil.FlagsFromMode(NetTransferMode.Reliable));
        }

        private void HandleModPacket(IModPacket packet)
        {
            switch (packet)
            {
                case SyncFullListPacket fullList:
                    PlayerNameRegistry.RemoteNames.Clear();
                    foreach (var kvp in fullList.Players)
                        PlayerNameRegistry.RemoteNames[kvp.Key] = kvp.Value;
                    _logger.Info($"[DirectClient] 已同步全员名单，共 {fullList.Players.Count} 人");
                    break;

                case SyncSinglePacket single:
                    PlayerNameRegistry.RemoteNames[single.PlayerId] = single.PlayerName;
                    _logger.Info($"[DirectClient] 收到新玩家加入消息: {single.PlayerName} (ID: {single.PlayerId})");
                    break;

                case SyncRemovePacket remove:
                    if (PlayerNameRegistry.RemoteNames.Remove(remove.PlayerId))
                        _logger.Info($"[DirectClient] 玩家离开，已移除名字缓存 (ID: {remove.PlayerId})");
                    break;
            }
        }

        public override void SendMessageToHost(byte[] bytes, int length, NetTransferMode mode, int channel = 0)
        {
            if (!_isConnected || _peer == null) return;
            
            var packet = ENetPacket.FromAppMessage(bytes, length);
            _peer.Send(channel, packet.AllBytes, ENetUtil.FlagsFromMode(mode));
        }

        public override void DisconnectFromHost(NetError reason, bool now = false)
        {
            if (!_isConnected && _connection == null) return;
            
            _isConnected = false;
            PlayerNameRegistry.RemoteNames.Clear(); 
            try
            {
                if (_peer != null)
                {
                    if (now) _peer.PeerDisconnectNow();
                    else
                    {
                        var packet = ENetPacket.FromDisconnection(new ENetDisconnection { reason = reason });
                        _peer.Send(0, packet.AllBytes, 8);
                        _peer.PeerDisconnectLater();
                    }
                }
                _connection?.Flush();
                _connection?.Destroy();
            }
            catch (Exception ex) { _logger.Warn($"Exception during disconnect: {ex.Message}"); }
            finally { _peer = null; _connection = null; }

            _handler.OnDisconnectedFromHost(_hostNetId, new NetErrorInfo(reason, true));
        }

        public override string? GetRawLobbyIdentifier() => null;
    }
}