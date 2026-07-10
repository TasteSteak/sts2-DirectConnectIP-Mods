using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
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
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;

namespace DirectConnectIP.Patches.Network;

public static class OfflineTakeoverCore
{
    private static readonly MethodInfo EnqueueActionMethod = AccessTools.Method(typeof(ActionQueueSynchronizer), "EnqueueAction");
    private static readonly MethodInfo HostBroadcastMessageMethod = AccessTools.Method(typeof(NetHostGameService), "BroadcastMessage");
    private static readonly FieldInfo RunLobbyConnectedIdsField = AccessTools.Field(typeof(RunLobby), "_connectedPlayerIds");
    private const ulong OfflineTakeoverDelayMs = 8_000;
    private static readonly Dictionary<ulong, OfflinePeerState> OfflinePeers = [];
    private static readonly object OfflinePeersLock = new();
    private static readonly HashSet<string> ScheduledRetries = [];
    private static readonly object ScheduledRetriesLock = new();
    private static readonly HashSet<ulong> LoadedRunOfflinePlayerIds = [];
    private static readonly object LoadedRunOfflinePlayerIdsLock = new();

    public static bool IsTakeoverConfigEnabled() => ModEntry.Config is { EnableOfflineTakeover: true };
    
    public static bool IsDirectConnectActive { get; set; }

    public static bool IsGhost(ulong netId)
    {
        try
        {
            if (!IsTakeoverConfigEnabled()) return false;
            if (!IsDirectConnectActive) return false;
            if (IsLocalNetId(netId)) return false;
            if (RunManager.Instance is not { NetService: { } netService }) return false;
            if (netService.Type is not (NetGameType.Host or NetGameType.Client)) return false;

            RefreshInferredPeerState();
            return GetPeerTakeoverState(netId, out _) == PeerTakeoverState.Ghost;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsOfflineOrPending(ulong netId)
    {
        try
        {
            if (!IsTakeoverConfigEnabled()) return false;
            if (!IsDirectConnectActive) return false;
            if (IsLocalNetId(netId)) return false;
            if (RunManager.Instance is not { NetService: { } netService }) return false;
            if (netService.Type is not (NetGameType.Host or NetGameType.Client)) return false;

            RefreshInferredPeerState();
            return GetPeerTakeoverState(netId, out _) != PeerTakeoverState.Online;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsPendingGhost(ulong netId, out ulong remainingMs)
    {
        remainingMs = 0;
        try
        {
            if (!IsTakeoverConfigEnabled()) return false;
            if (!IsDirectConnectActive) return false;
            if (IsLocalNetId(netId)) return false;
            if (RunManager.Instance is not { NetService: { } netService }) return false;
            if (netService.Type is not (NetGameType.Host or NetGameType.Client)) return false;

            RefreshInferredPeerState();
            return GetPeerTakeoverState(netId, out remainingMs) == PeerTakeoverState.Pending;
        }
        catch
        {
            remainingMs = 0;
            return false;
        }
    }

    public static bool HasPendingGhost(IEnumerable<ulong> netIds, out ulong retryDelayMs)
    {
        retryDelayMs = 0;
        var hasPending = false;
        var minRemainingMs = ulong.MaxValue;

        foreach (var netId in netIds)
        {
            if (!IsPendingGhost(netId, out var remainingMs)) continue;

            hasPending = true;
            if (remainingMs < minRemainingMs)
            {
                minRemainingMs = remainingMs;
            }
        }

        if (!hasPending) return false;

        retryDelayMs = Math.Clamp(minRemainingMs + 150UL, 250UL, OfflineTakeoverDelayMs + 250UL);
        return true;
    }

    public static void ScheduleTakeoverRetry(object owner, string reason, ulong delayMs, Action callback)
    {
        if (!IsTakeoverConfigEnabled()) return;
        if (!IsDirectConnectActive) return;

        var key = $"{RuntimeHelpers.GetHashCode(owner)}:{reason}";
        lock (ScheduledRetriesLock)
        {
            if (!ScheduledRetries.Add(key)) return;
        }

        var context = SynchronizationContext.Current;
        _ = RunScheduledRetryAsync(key, delayMs, context, callback);
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
        MarkPeerDisconnectedCore(netId, reason, preserveExistingDisconnectTime: reason == NetError.RunInProgress);
    }

    public static void MarkPeerDisconnectedImmediate(ulong netId, NetError reason, string context)
    {
        MarkPeerDisconnectedCore(netId, reason, preserveExistingDisconnectTime: false, immediateTakeover: true, context: context);
    }

    private static void MarkPeerDisconnectedCore(
        ulong netId,
        NetError reason,
        bool preserveExistingDisconnectTime,
        bool immediateTakeover = false,
        string context = null)
    {
        if (!IsTakeoverConfigEnabled()) return;
        if (IsLocalNetId(netId)) return;

        lock (OfflinePeersLock)
        {
            var wasTracked = OfflinePeers.TryGetValue(netId, out var state);
            var wasTransportConnected = state?.TransportConnected == true;
            if (!wasTracked)
            {
                state = new OfflinePeerState();
                OfflinePeers[netId] = state;
            }

            if (immediateTakeover)
            {
                var now = Time.GetTicksMsec();
                state.DisconnectedAtMsec = now > OfflineTakeoverDelayMs ? now - OfflineTakeoverDelayMs : 1UL;
                state.TakeoverLogged = false;
            }
            else if (!preserveExistingDisconnectTime || state.DisconnectedAtMsec == 0 || state.TransportConnected)
            {
                state.DisconnectedAtMsec = Time.GetTicksMsec();
                state.TakeoverLogged = false;
            }

            state.TransportConnected = false;
            state.LastDisconnectReason = reason;

            if (immediateTakeover)
            {
                Log.Warn($"[DirectConnectIP] 玩家 {netId} 已在存档载入时判定为离线，立即允许托管。原因: {reason} {context}");
            }
            else if (!wasTracked || wasTransportConnected)
            {
                Log.Warn($"[DirectConnectIP] 玩家 {netId} 已标记为离线，等待 {OfflineTakeoverDelayMs}ms 后允许托管。原因: {reason}");
            }
        }
    }

    public static void MarkPeerTransportConnected(ulong netId)
    {
        if (!IsTakeoverConfigEnabled()) return;
        if (IsLocalNetId(netId)) return;

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

        lock (LoadedRunOfflinePlayerIdsLock)
        {
            LoadedRunOfflinePlayerIds.Remove(netId);
        }
    }

    public static void ClearPeerState()
    {
        lock (OfflinePeersLock)
        {
            OfflinePeers.Clear();
        }

        lock (ScheduledRetriesLock)
        {
            ScheduledRetries.Clear();
        }

        lock (LoadedRunOfflinePlayerIdsLock)
        {
            LoadedRunOfflinePlayerIds.Clear();
        }
    }

    public static IReadOnlyList<ulong> RememberLoadedRunMissingPlayers(
        SerializableRun run,
        IEnumerable<ulong> connectedPlayerIds,
        string context)
    {
        if (!IsTakeoverConfigEnabled()) return Array.Empty<ulong>();
        if (!IsDirectConnectActive) return Array.Empty<ulong>();
        if (run?.Players == null) return Array.Empty<ulong>();

        var connected = connectedPlayerIds?.ToHashSet() ?? [];
        AddLocalObservedId(connected);

        if (RunManager.Instance is { NetService: { } netService })
        {
            connected.Add(netService.NetId);
            if (netService is NetClientGameService clientService)
            {
                connected.Add(clientService.HostNetId);
            }
        }

        var missing = new List<ulong>();
        foreach (var player in run.Players)
        {
            var netId = player.NetId;
            if (IsLocalNetId(netId)) continue;
            if (connected.Contains(netId))
            {
                MarkPeerRejoined(netId);
                continue;
            }

            missing.Add(netId);
            MarkPeerDisconnectedImmediate(netId, NetError.Quit, context);
        }

        if (missing.Count == 0) return missing;

        lock (LoadedRunOfflinePlayerIdsLock)
        {
            foreach (var netId in missing)
            {
                LoadedRunOfflinePlayerIds.Add(netId);
            }
        }

        return missing;
    }

    public static void ApplyLoadedRunOfflinePlayersToRunLobby(RunLobby runLobby, RunState state)
    {
        if (!IsTakeoverConfigEnabled()) return;
        if (!IsDirectConnectActive) return;
        if (runLobby == null || state?.Players == null) return;

        HashSet<ulong> offlineIds;
        lock (LoadedRunOfflinePlayerIdsLock)
        {
            offlineIds = LoadedRunOfflinePlayerIds.ToHashSet();
        }

        if (offlineIds.Count == 0) return;

        var runPlayerIds = state.Players.Select(p => p.NetId).ToHashSet();
        if (RunLobbyConnectedIdsField?.GetValue(runLobby) is not HashSet<ulong> connectedIds)
        {
            Log.Warn("[DirectConnectIP] 找不到 RunLobby 连接玩家集合，无法剔除存档离线玩家。");
            return;
        }

        foreach (var netId in offlineIds)
        {
            if (!runPlayerIds.Contains(netId)) continue;

            if (connectedIds.Remove(netId))
            {
                Log.Warn($"[DirectConnectIP] 存档载入时将离线玩家 {netId} 从运行大厅连接列表移除。");
            }

            MarkPeerDisconnectedImmediate(netId, NetError.Quit, "loaded-run-lobby");
        }

        lock (LoadedRunOfflinePlayerIdsLock)
        {
            LoadedRunOfflinePlayerIds.Clear();
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

    public static bool BroadcastGhostMessageToClients<TMessage>(TMessage message, ulong ghostNetId)
    {
        if (!IsTakeoverConfigEnabled()) return false;

        try
        {
            if (RunManager.Instance is not { NetService: NetHostGameService hostService }) return false;
            if (HostBroadcastMessageMethod == null)
            {
                Log.Error("[DirectConnectIP] 找不到 NetHostGameService.BroadcastMessage，幽灵事件同步失败！");
                return false;
            }

            var messageType = message?.GetType();
            if (messageType == null) return false;

            var method = HostBroadcastMessageMethod.IsGenericMethod
                ? HostBroadcastMessageMethod.MakeGenericMethod(messageType)
                : HostBroadcastMessageMethod;
            method.Invoke(hostService, [message, ghostNetId, 0, ghostNetId]);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"[DirectConnectIP] 幽灵玩家 {ghostNetId} 消息同步失败: {ex}");
            return false;
        }
    }

    private static bool IsOfflineLongEnoughForTakeover(ulong netId)
    {
        return GetPeerTakeoverState(netId, out _) == PeerTakeoverState.Ghost;
    }

    private static PeerTakeoverState GetPeerTakeoverState(ulong netId, out ulong remainingMs)
    {
        remainingMs = 0;
        lock (OfflinePeersLock)
        {
            if (!OfflinePeers.TryGetValue(netId, out var state))
            {
                return PeerTakeoverState.Online;
            }

            if (state.DisconnectedAtMsec == 0)
            {
                return PeerTakeoverState.Online;
            }

            if (state.TransportConnected)
            {
                return PeerTakeoverState.Online;
            }

            var elapsed = Time.GetTicksMsec() - state.DisconnectedAtMsec;
            if (elapsed < OfflineTakeoverDelayMs)
            {
                remainingMs = OfflineTakeoverDelayMs - elapsed;
                return PeerTakeoverState.Pending;
            }

            if (!state.TakeoverLogged)
            {
                state.TakeoverLogged = true;
                Log.Warn($"[DirectConnectIP] 玩家 {netId} 已确认断开 {elapsed}ms，进入离线托管判定。原因: {state.LastDisconnectReason}");
            }

            return PeerTakeoverState.Ghost;
        }
    }

    private static void RefreshInferredPeerState()
    {
        if (!IsTakeoverConfigEnabled()) return;
        if (!IsDirectConnectActive) return;
        if (RunManager.Instance is not { NetService: { } netService } runManager) return;
        if (!runManager.IsInProgress) return;

        var runState = runManager.DebugOnlyGetState();
        if (runState?.Players == null || runState.Players.Count <= 1) return;

        var onlineIds = GetObservedOnlineIds(netService);
        foreach (var player in runState.Players)
        {
            var playerId = player.NetId;
            if (IsLocalNetId(playerId)) continue;

            if (onlineIds.Contains(playerId))
            {
                MarkPeerTransportConnected(playerId);
            }
            else
            {
                MarkPeerDisconnectedCore(playerId, NetError.Quit, preserveExistingDisconnectTime: true);
            }
        }
    }

    private static HashSet<ulong> GetObservedOnlineIds(INetGameService netService)
    {
        var ids = new HashSet<ulong>();

        if (LocalContext.NetId.HasValue)
            ids.Add(LocalContext.NetId.Value);
        else if (ModEntry.Config != null)
            ids.Add(ModEntry.Config.LocalPlayerId);

        switch (netService)
        {
            case NetHostGameService hostService:
                ids.Add(hostService.NetId);
                foreach (var peer in hostService.ConnectedPeers)
                {
                    if (peer.readyForBroadcasting)
                    {
                        ids.Add(peer.peerId);
                    }
                }
                break;
            case NetClientGameService clientService:
                ids.Add(clientService.NetId);
                ids.Add(clientService.HostNetId);
                if (RunManager.Instance.RunLobby is RunLobby runLobby)
                {
                    foreach (var id in runLobby.ConnectedPlayerIds)
                    {
                        ids.Add(id);
                    }
                }
                break;
        }

        return ids;
    }

    private static void AddLocalObservedId(HashSet<ulong> ids)
    {
        if (LocalContext.NetId.HasValue)
            ids.Add(LocalContext.NetId.Value);
        else if (ModEntry.Config != null)
            ids.Add(ModEntry.Config.LocalPlayerId);
    }

    private static bool IsLocalNetId(ulong netId)
    {
        if (LocalContext.NetId == netId) return true;
        return ModEntry.Config != null && ModEntry.Config.LocalPlayerId == netId;
    }

    private static async Task RunScheduledRetryAsync(string key, ulong delayMs, SynchronizationContext context, Action callback)
    {
        var postedOrRan = false;
        try
        {
            await Task.Delay((int)Math.Clamp(delayMs, 250UL, OfflineTakeoverDelayMs + 250UL));
            if (!IsTakeoverConfigEnabled() || !IsDirectConnectActive) return;

            if (context != null)
            {
                context.Post(_ => RunScheduledRetryCallback(key, callback), null);
                postedOrRan = true;
            }
            else
            {
                RunScheduledRetryCallback(key, callback);
                postedOrRan = true;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[DirectConnectIP] 离线托管延迟重试失败: {ex}");
        }
        finally
        {
            if (!postedOrRan)
            {
                lock (ScheduledRetriesLock)
                {
                    ScheduledRetries.Remove(key);
                }
            }
        }
    }

    private static void RunScheduledRetryCallback(string key, Action callback)
    {
        lock (ScheduledRetriesLock)
        {
            ScheduledRetries.Remove(key);
        }

        try
        {
            callback();
        }
        catch (Exception ex)
        {
            Log.Error($"[DirectConnectIP] 离线托管重试执行失败: {ex}");
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

    private enum PeerTakeoverState
    {
        Online,
        Pending,
        Ghost
    }
}
