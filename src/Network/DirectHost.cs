#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DirectConnectIP.Network.Packets;
using DirectConnectIP.Patches.Network;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Multiplayer.Transport.ENet;

namespace DirectConnectIP.Network;

public class DirectHost(INetHostHandler handler) : NetHost(handler)
{
    private readonly MegaCrit.Sts2.Core.Logging.Logger _logger = new (nameof(DirectHost), LogType.Network);
    private ENetConnection? _connection;
    private readonly List<ClientConnection> _connectedPeers = [];
    private readonly List<PendingHandshake> _pendingHandshakes = [];
        
    private readonly ConcurrentQueue<Action> _mainThreadActions = new();
        
    private bool _isConnected;
    private ulong _hostNetId;

    public override bool IsConnected => _isConnected;
    public override ulong NetId => _hostNetId;
    public override IEnumerable<ulong> ConnectedPeerIds => _connectedPeers.Select(c => c.NetId);

    public NetErrorInfo? StartHost(ushort port, int maxClients, ulong hostNetId)
    {
        _hostNetId = hostNetId;
        if (_connection != null) { _connection.Destroy(); _connection = null; }
        _connection = new ENetConnection();
    
        var err = _connection.CreateHostBound("*", port, maxClients);
        if (err != Error.Ok)
        {
            _logger.Error($"Failed to create host: {err}");
            _connection.Destroy();
            _connection = null;
            return new NetErrorInfo(err);
        }
    
        _isConnected = true;
        _logger.Info($"DirectHost started on port {port} with ID {hostNetId}");
        return null;
    }

    public override void Update()
    {
        while (_mainThreadActions.TryDequeue(out var action)) action();

        if (_connection == null) return;

        while (_connection.TryService(out var data))
        {
            switch (data!.Value.type)
            {
                case ENetConnection.EventType.Error:
                    _logger.Error($"ENet error: {data.Value.error}");
                    break;
                case ENetConnection.EventType.Connect:
                    _logger.Debug("New client connected, waiting for handshake");
                    data.Value.peer.SetTimeout(24, 20000, 20000);
                    break;
                case ENetConnection.EventType.Disconnect:
                    HandleDisconnect(data.Value.peer, NetError.Quit, notifyHandler: true);
                    break;
                case ENetConnection.EventType.Receive:
                    HandlePacketReceived(data.Value);
                    break;
                case ENetConnection.EventType.None:
                    break;
            }
        }

        var now = (long)Time.GetTicksMsec();
        foreach (var pending in _pendingHandshakes.ToList().Where(pending => now - pending.ReceivedMsec > 10000))
        {
            _logger.Warn($"Handshake timeout for peer");
            try { pending.Peer.Reset(); }
            catch
            {
                // ignored
            }

            _pendingHandshakes.Remove(pending);
        }
    }

    private void HandlePacketReceived(ENetServiceData data)
    {
        var peer = data.peer;
        try
        {
            var packet = new ENetPacket(data.packetData);

            switch (packet.PacketType)
            {
                case ENetPacketType.HandshakeRequest:
                {
                    var req = packet.AsHandshakeRequest();
                    if (req.netId == _hostNetId || 
                        _connectedPeers.Any(c => c.NetId == req.netId) || 
                        _pendingHandshakes.Any(p => p.NetId == req.netId))
                    {
                        var rejectResp = ENetPacket.FromHandshakeResponse(new ENetHandshakeResponse
                        {
                            netId = req.netId, status = ENetHandshakeStatus.IdCollision
                        });
                        peer.Send(0, rejectResp.AllBytes, 1);
                        peer.PeerDisconnectLater(); 
                        return;
                    }

                    _pendingHandshakes.Add(new PendingHandshake { Peer = peer, NetId = req.netId, ReceivedMsec = (long)Time.GetTicksMsec() });
                    _ = DelayedHandshakeQueue(peer, req.netId);
                    break;
                }
                case ENetPacketType.Disconnection:
                {
                    var dis = packet.AsDisconnection();
                    HandleDisconnect(peer, dis.reason, notifyHandler: true);
                    break;
                }
                case ENetPacketType.HandshakeResponse:
                case ENetPacketType.ApplicationMessage:
                default:
                {
                    var conn = GetConnectionByPeer(peer);
                    if (conn.HasValue)
                    {
                        var rawData = packet.AsAppMessage();
                        if (data.channel == ModPacketRouter.Channel && ModPacketRouter.IsModPacket(rawData))
                        {
                            var modPacket = ModPacketRouter.Deserialize(rawData);
                            if (modPacket != null) HandleModPacket(conn.Value.NetId, modPacket);
                            return; 
                        }
                        
                        _handler.OnPacketReceived(conn.Value.NetId, rawData, data.mode, data.channel);
                    }
                    else { _logger.Error("Received non-handshake packet from unknown peer"); }
                    break;
                }
            }
        }
        catch (Exception ex) { _logger.Warn($"Failed to process packet from peer: {ex.Message}"); }
    }

    private void SendModPacketToClient(ulong peerId, IModPacket packet)
    {
        var conn = GetConnectionById(peerId);
        if (!conn.HasValue) return;
        var packetData = ModPacketRouter.Serialize(packet);
        var enetPacket = ENetPacket.FromAppMessage(packetData, packetData.Length);
        conn.Value.Peer.Send(ModPacketRouter.Channel, enetPacket.AllBytes, ENetUtil.FlagsFromMode(NetTransferMode.Reliable));
    }

    private void BroadcastModPacket(IModPacket packet, ulong? excludeId = null)
    {
        var packetData = ModPacketRouter.Serialize(packet);
        var enetPacket = ENetPacket.FromAppMessage(packetData, packetData.Length);
        foreach (var conn in _connectedPeers.Where(conn => !excludeId.HasValue || conn.NetId != excludeId.Value))
        {
            conn.Peer.Send(ModPacketRouter.Channel, enetPacket.AllBytes, ENetUtil.FlagsFromMode(NetTransferMode.Reliable));
        }
    }

    private void HandleModPacket(ulong senderId, IModPacket packet)
    {
        if (packet is not SyncClientNamePacket namePacket) return;
        PlayerNameRegistry.RemoteNames[senderId] = namePacket.PlayerName;

        var fullList = new SyncFullListPacket();
        foreach (var kvp in PlayerNameRegistry.RemoteNames) fullList.Players[kvp.Key] = kvp.Value;
        fullList.Players[ModEntry.Config.LocalPlayerId] = ModEntry.Config.LocalPlayerName;
        SendModPacketToClient(senderId, fullList);

        BroadcastModPacket(new SyncSinglePacket(senderId, namePacket.PlayerName), excludeId: senderId);
    }

    private void HandleDisconnect(ENetPacketPeer? peer, NetError reason, bool notifyHandler)
    {
        if (peer == null) return;
            
        var conn = GetConnectionByPeer(peer);
        if (conn.HasValue)
        {
            _connectedPeers.Remove(conn.Value);
            if (notifyHandler)
                _handler.OnPeerDisconnected(conn.Value.NetId, new NetErrorInfo(reason, false));
                
            PlayerNameRegistry.RemoteNames.Remove(conn.Value.NetId);
            BroadcastModPacket(new SyncRemovePacket(conn.Value.NetId));

            _logger.Debug($"Client {conn.Value.NetId} disconnected: {reason}");
        }
        else
        {
            var pending = _pendingHandshakes.FirstOrDefault(p => p.Peer == peer);
            if (pending.Peer == null) return;
            _pendingHandshakes.Remove(pending);
            _logger.Debug($"Pending client {pending.NetId} disconnected: {reason}");
        }
    }

    private async Task DelayedHandshakeQueue(ENetPacketPeer peer, ulong clientNetId)
    {
        try { await Task.Delay(10); }
        catch
        {
            // ignored
        }

        _mainThreadActions.Enqueue(() => CompleteHandshakeSync(peer, clientNetId));
    }

    private void CompleteHandshakeSync(ENetPacketPeer peer, ulong clientNetId)
    {
        var successResp = ENetPacket.FromHandshakeResponse(new ENetHandshakeResponse
        {
            netId = _hostNetId, status = ENetHandshakeStatus.Success
        });
        peer.Send(0, successResp.AllBytes, 1);

        var pending = _pendingHandshakes.FirstOrDefault(p => p.Peer == peer);
        if (pending.Peer == null) 
        {
            _logger.Warn($"客户端 {clientNetId} 在握手延迟期间已断开，中止连接建立。");
            return;
        }
        _pendingHandshakes.Remove(pending);
        _connectedPeers.Add(new ClientConnection { Peer = peer, NetId = clientNetId });
        
        _handler.OnPeerConnected(clientNetId);
        SystemPreheater.PrewarmPlayer(clientNetId);

        _logger.Debug($"Handshake completed for client {clientNetId}. I am {_hostNetId}");
    }

    private ClientConnection? GetConnectionByPeer(ENetPacketPeer peer)
    {
        var index = _connectedPeers.FindIndex(c => c.Peer == peer);
        return index >= 0 ? _connectedPeers[index] : null;
    }

    private ClientConnection? GetConnectionById(ulong id)
    {
        var index = _connectedPeers.FindIndex(c => c.NetId == id);
        return index >= 0 ? _connectedPeers[index] : null;
    }

    public override void SendMessageToClient(ulong peerId, byte[] bytes, int length, NetTransferMode mode, int channel = 0)
    {
        var conn = GetConnectionById(peerId);
        if (!conn.HasValue) return;
        var packet = ENetPacket.FromAppMessage(bytes, length);
        conn.Value.Peer.Send(channel, packet.AllBytes, ENetUtil.FlagsFromMode(mode));
    }

    public override void SendMessageToAll(byte[] bytes, int length, NetTransferMode mode, int channel = 0)
    {
        foreach (var conn in _connectedPeers)
        {
            var packet = ENetPacket.FromAppMessage(bytes, length);
            conn.Peer.Send(channel, packet.AllBytes, ENetUtil.FlagsFromMode(mode));
        }
    }

    public override void DisconnectClient(ulong peerId, NetError reason, bool now = false)
    {
        var conn = GetConnectionById(peerId);
        if (!conn.HasValue) return;

        if (now) { conn.Value.Peer.PeerDisconnectNow(); }
        else
        {
            var packet = ENetPacket.FromDisconnection(new ENetDisconnection { reason = reason });
            conn.Value.Peer.Send(0, packet.AllBytes, 1);
            conn.Value.Peer.PeerDisconnectLater();
        }
        _connectedPeers.Remove(conn.Value);
        _handler.OnPeerDisconnected(peerId, new NetErrorInfo(reason, true));
    }

    public override void StopHost(NetError reason, bool now = false)
    {
        var peersToDisconnect = _connectedPeers.ToList();
        foreach (var conn in peersToDisconnect) DisconnectClient(conn.NetId, reason, now);

        if (_connection != null) {
            _connection.Flush();
            _connection.Destroy();
            _connection = null;
        }
        _isConnected = false;
        _handler.OnDisconnected(new NetErrorInfo(reason, true));
    }

    public override void SetHostIsClosed(bool isClosed) { }
    public override string? GetRawLobbyIdentifier() => null;

    private record struct ClientConnection { public ENetPacketPeer Peer; public ulong NetId; }
    private record struct PendingHandshake { public ENetPacketPeer Peer; public ulong NetId; public long ReceivedMsec; }
}