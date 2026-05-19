using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;

namespace DirectConnectIP.Patches.Network;

public static class OfflineTakeoverCore
{
    private static readonly MethodInfo EnqueueActionMethod = AccessTools.Method(typeof(ActionQueueSynchronizer), "EnqueueAction");
    private const ulong OfflineTakeoverDelayMs = 8_000;
    private static readonly Dictionary<ulong, OfflinePeerState> OfflinePeers = [];
    private static readonly object OfflinePeersLock = new();

    public static bool IsTakeoverConfigEnabled() => ModEntry.Config is { EnableOfflineTakeover: true };
    
    public static bool IsDirectConnectActive { get; set; }

    public static bool IsGhost(ulong netId)
    {
        try
        {
            if (!IsTakeoverConfigEnabled()) return false;
            if (!IsDirectConnectActive) return false;
            if (LocalContext.NetId == netId) return false;
            if (RunManager.Instance is not { NetService: { } netService }) return false;
            if (netService.Type is not (NetGameType.Host or NetGameType.Client)) return false;

            return IsOfflineLongEnoughForTakeover(netId);
        }
        catch
        {
            return false;
        }
    }

    private static HashSet<ulong> GetBroadcastReadyIds()
    {
        var ids = new HashSet<ulong>();

        if (LocalContext.NetId.HasValue)
            ids.Add(LocalContext.NetId.Value);
        else if (ModEntry.Config != null)
            ids.Add(ModEntry.Config.LocalPlayerId);

        if (RunManager.Instance is not { NetService: { IsConnected: true } netService })
            return ids;

        if (netService.Type == NetGameType.Host && netService is NetHostGameService hostService)
        {
            foreach (var peer in hostService.ConnectedPeers)
            {
                if (peer.readyForBroadcasting)
                {
                    ids.Add(peer.peerId);
                }
            }
        }
        else if (netService.Type == NetGameType.Client && RunManager.Instance.RunLobby is RunLobby runLobby)
        {
            foreach (var id in runLobby.ConnectedPlayerIds)
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    public static void MarkPeerDisconnected(ulong netId, NetError reason)
    {
        if (!IsTakeoverConfigEnabled()) return;
        if (LocalContext.NetId == netId) return;

        lock (OfflinePeersLock)
        {
            if (!OfflinePeers.TryGetValue(netId, out var state))
            {
                state = new OfflinePeerState();
                OfflinePeers[netId] = state;
            }

            if (reason != NetError.RunInProgress || state.DisconnectedAtMsec == 0)
            {
                state.DisconnectedAtMsec = Time.GetTicksMsec();
                state.TakeoverLogged = false;
            }

            state.TransportConnected = false;
            state.LastDisconnectReason = reason;
        }
    }

    public static void MarkPeerTransportConnected(ulong netId)
    {
        if (!IsTakeoverConfigEnabled()) return;
        if (LocalContext.NetId == netId) return;

        lock (OfflinePeersLock)
        {
            if (!OfflinePeers.TryGetValue(netId, out var state))
            {
                return;
            }

            state.TransportConnected = true;
            state.TransportConnectedAtMsec = Time.GetTicksMsec();
            state.TakeoverLogged = false;
        }
    }

    public static void MarkPeerRejoined(ulong netId)
    {
        lock (OfflinePeersLock)
        {
            OfflinePeers.Remove(netId);
        }
    }

    public static void ClearPeerState()
    {
        lock (OfflinePeersLock)
        {
            OfflinePeers.Clear();
        }
    }

    public static bool ShouldRejectRunningRejoin(ulong netId, out NetError reason, out string detail)
    {
        reason = NetError.RunInProgress;
        detail = string.Empty;

        if (!IsTakeoverConfigEnabled()) return false;
        if (!IsDirectConnectActive) return false;

        var runState = RunManager.Instance.DebugOnlyGetState();
        if (runState?.CurrentRoomCount > 0)
        {
            var hadTakeover = false;
            lock (OfflinePeersLock)
            {
                hadTakeover = OfflinePeers.TryGetValue(netId, out var state) && state.CombatTakeoverAdvanced;
            }

            detail = hadTakeover
                ? "玩家断线后战斗托管已经推进过战斗状态，当前版本不会让旧战斗状态客户端直接重连。"
                : "当前处于房间内瞬态流程，运行中重连暂不安全。";
            return true;
        }

        if (runState != null)
        {
            var onlineIds = GetBroadcastReadyIds();
            foreach (var player in runState.Players)
            {
                if (player.NetId == netId) continue;
                if (onlineIds.Contains(player.NetId)) continue;

                detail = $"玩家 {player.NetId} 尚未处于官方广播就绪状态，不能让 {netId} 进行运行中重连。";
                return true;
            }
        }

        return false;
    }

    public static void EnqueueGhostAction(GameAction action, ulong ghostNetId)
    {
        if (!IsTakeoverConfigEnabled()) return;

        try
        {
            if (RunManager.Instance is not { ActionQueueSynchronizer: { } sync }) return;

            if (EnqueueActionMethod != null)
            {
                EnqueueActionMethod.Invoke(sync, [action, ghostNetId]);
                MarkTakeoverAdvanced(ghostNetId);
            }
            else
            {
                Log.Error("[DirectConnectIP] 找不到 EnqueueAction 方法，代管发包失败！");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[DirectConnectIP] 动作 {action} 发包失败: {ex}");
        }
    }

    private static bool IsOfflineLongEnoughForTakeover(ulong netId)
    {
        lock (OfflinePeersLock)
        {
            if (!OfflinePeers.TryGetValue(netId, out var state))
            {
                return false;
            }

            if (state.DisconnectedAtMsec == 0)
            {
                return false;
            }

            if (state.TransportConnected)
            {
                return false;
            }

            var elapsed = Time.GetTicksMsec() - state.DisconnectedAtMsec;
            var isGhost = elapsed >= OfflineTakeoverDelayMs;
            if (isGhost && !state.TakeoverLogged)
            {
                state.TakeoverLogged = true;
                Log.Warn($"[DirectConnectIP] 玩家 {netId} 已确认断开 {elapsed}ms，进入离线托管判定。原因: {state.LastDisconnectReason}");
            }

            return isGhost;
        }
    }

    private static void MarkTakeoverAdvanced(ulong netId)
    {
        lock (OfflinePeersLock)
        {
            if (!OfflinePeers.TryGetValue(netId, out var state))
            {
                state = new OfflinePeerState { DisconnectedAtMsec = Time.GetTicksMsec() };
                OfflinePeers[netId] = state;
            }

            if (CombatManager.Instance.IsInProgress)
            {
                state.CombatTakeoverAdvanced = true;
            }
        }
    }

    private sealed class OfflinePeerState
    {
        public ulong DisconnectedAtMsec;
        public bool TransportConnected;
        public ulong TransportConnectedAtMsec;
        public bool CombatTakeoverAdvanced;
        public bool TakeoverLogged;
        public NetError LastDisconnectReason;
    }
}
