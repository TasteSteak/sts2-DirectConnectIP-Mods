#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace DirectConnectIP.Patches.Network;

[HarmonyPatch(typeof(RunLobby), "HandleClientRejoinRequestMessage")]
public static class RunningRejoinGuardPatch
{
    static bool Prepare() => OfflineTakeoverCore.IsTakeoverConfigEnabled();

    public static bool Prefix(ClientRejoinRequestMessage message, ulong senderId, out bool __state)
    {
        __state = false;
        if (!OfflineTakeoverCore.IsDirectConnectActive) return true;
        if (!OfflineTakeoverCore.ShouldRejectRunningRejoin(senderId, out var reason, out var detail))
        {
            __state = RunManager.Instance.DebugOnlyGetState()?.Players.Any(p => p.NetId == senderId) == true;
            return true;
        }

        Log.Warn($"[DirectConnectIP] 拒绝玩家 {senderId} 运行中重连：{detail}");
        if (RunManager.Instance.NetService is NetHostGameService hostService)
        {
            hostService.DisconnectClient(senderId, reason);
        }

        return false;
    }

    public static void Postfix(ulong senderId, bool __state)
    {
        if (!__state) return;
        if (!OfflineTakeoverCore.IsDirectConnectActive) return;
        if (OfflineTakeoverCore.ShouldRejectRunningRejoin(senderId, out _, out _)) return;

        OfflineTakeoverCore.MarkPeerRejoined(senderId);
    }
}

[HarmonyPatch(typeof(RunLobby), "HandlePlayerLeftMessage")]
public static class RunLobbyPeerLeftTakeoverStatePatch
{
    static bool Prepare() => OfflineTakeoverCore.IsTakeoverConfigEnabled();

    public static void Postfix(PlayerLeftMessage message)
    {
        OfflineTakeoverCore.MarkPeerDisconnected(message.playerId, NetError.Quit);
    }
}

[HarmonyPatch(typeof(RunLobby), "HandlePlayerRejoinedMessage")]
public static class RunLobbyPeerRejoinedTakeoverStatePatch
{
    static bool Prepare() => OfflineTakeoverCore.IsTakeoverConfigEnabled();

    public static void Postfix(PlayerRejoinedMessage message)
    {
        OfflineTakeoverCore.MarkPeerRejoined(message.playerId);
    }
}

[HarmonyPatch(typeof(PlayCardAction), "ExecuteAction")]
public static class PlayCardActionGhostPatch
{
    static bool Prepare() => OfflineTakeoverCore.IsTakeoverConfigEnabled();

    public static void Prefix(PlayCardAction __instance, out IDisposable? __state)
    {
        __state = null;
        if (!OfflineTakeoverCore.IsDirectConnectActive) return;

        if (OfflineTakeoverCore.IsGhost(__instance.OwnerId))
        {
            __state = CardSelectCmd.PushSelector(new VakuuCardSelector());
        }
    }

    public static void Postfix(Task? __result, IDisposable? __state)
    {
        if (!OfflineTakeoverCore.IsDirectConnectActive) return;

        if (__state != null && __result != null)
        {
            if (System.Threading.SynchronizationContext.Current != null)
            {
                __result.ContinueWith(_ => __state.Dispose(), TaskScheduler.FromCurrentSynchronizationContext());
            }
            else
            {
                __result.ContinueWith(_ => __state.Dispose());
            }
        }
        else
        {
            __state?.Dispose();
        }
    }
}

// ==========================================
// 战斗托管：后台并发出牌
// ==========================================
[HarmonyPatch(typeof(ActionQueueSynchronizer), nameof(ActionQueueSynchronizer.SetCombatState))]
public static class ConcurrentAutoPlayTurnPatch
{
    private static readonly HashSet<ulong> ActiveAiTasks = [];
    static bool Prepare() => OfflineTakeoverCore.IsTakeoverConfigEnabled();

    public static void Postfix(ActionQueueSynchronizer __instance, ActionSynchronizerCombatState combatState)
    {
        if (!OfflineTakeoverCore.IsDirectConnectActive) return;
        if (RunManager.Instance.NetService.Type != NetGameType.Host) return;
        if (combatState != ActionSynchronizerCombatState.PlayPhase) return;
        
        var state = CombatManager.Instance.DebugOnlyGetState();
        if (state == null) return;

        var roundNumber = state.RoundNumber;

        foreach (var p in state.Players)
        {
            if (!OfflineTakeoverCore.IsGhost(p.NetId) || !p.Creature.IsAlive) continue;
            if (!ActiveAiTasks.Add(p.NetId)) continue;

            RunGhostAiTask(p, roundNumber);
        }
    }

    private static async void RunGhostAiTask(Player ghostPlayer, int roundNumber)
    {
        try
        {
            var pendingCards = new HashSet<CardModel>();
            while (RunManager.Instance.IsInProgress && CombatManager.Instance.IsInProgress)
            {
                if (!OfflineTakeoverCore.IsDirectConnectActive) break; 

                var combatState = ghostPlayer.Creature.CombatState;
                if (combatState == null || combatState.RoundNumber != roundNumber) break;
                if (CombatManager.Instance.IsPlayerReadyToEndTurn(ghostPlayer)) break;

                var handPile = PileType.Hand.GetPile(ghostPlayer);
                
                var playableCards = handPile.Cards.Where(c => c.CanPlay(out _, out _)).ToList();
                var cardToPlay = playableCards.FirstOrDefault(c => !pendingCards.Contains(c));

                if (cardToPlay != null)
                {
                    var target = cardToPlay.TargetType switch
                    {
                        TargetType.AnyEnemy => combatState.HittableEnemies.Count > 0 ? combatState.HittableEnemies[0] : null,
                        TargetType.AnyPlayer => ghostPlayer.Creature,
                        TargetType.AnyAlly => combatState.Allies.FirstOrDefault(c =>
                            c is { IsAlive: true, IsPlayer: true } && c != ghostPlayer.Creature),
                        _ => null
                    };
                    pendingCards.Add(cardToPlay);
                    var playAction = new PlayCardAction(cardToPlay, target);
                    
                    OfflineTakeoverCore.EnqueueGhostAction(playAction, ghostPlayer.NetId);

                    await Task.Delay(1200); 
                }
                else if (playableCards.Count > 0)
                {
                    await Task.Delay(800);
                }
                else
                {
                    OfflineTakeoverCore.EnqueueGhostAction(new EndPlayerTurnAction(ghostPlayer, roundNumber), ghostPlayer.NetId);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[DirectConnectIP] 幽灵后台 AI 发生异常: {ex}");
            if (!CombatManager.Instance.IsPlayerReadyToEndTurn(ghostPlayer))
            {
                OfflineTakeoverCore.EnqueueGhostAction(new EndPlayerTurnAction(ghostPlayer, roundNumber), ghostPlayer.NetId);
            }
        }
        finally
        {
            ActiveAiTasks.Remove(ghostPlayer.NetId);
        }
    }
}

// ==========================================
// 战斗托管 (2/2)
// ==========================================
[HarmonyPatch(typeof(ActionQueueSynchronizer), nameof(ActionQueueSynchronizer.SetCombatState))]
public static class AutoReadyEnemyTurnPatch
{
    private static readonly FieldInfo? ReadyPlayersField = AccessTools.Field(typeof(CombatManager), "_playersReadyToBeginEnemyTurn");

    static bool Prepare() => OfflineTakeoverCore.IsTakeoverConfigEnabled();

    public static void Postfix(ActionQueueSynchronizer __instance, ActionSynchronizerCombatState combatState)
    {
        if (!OfflineTakeoverCore.IsDirectConnectActive) return;
        if (RunManager.Instance.NetService.Type != NetGameType.Host) return;
        
        if (combatState != ActionSynchronizerCombatState.EndTurnPhaseOne) return;
        
        var state = CombatManager.Instance.DebugOnlyGetState();
        if (state == null) return;

        var readySet = ReadyPlayersField?.GetValue(CombatManager.Instance) as HashSet<Player>;
        
        foreach (var p in state.Players)
        {
            if (!OfflineTakeoverCore.IsGhost(p.NetId) || !p.Creature.IsAlive) continue;
            if (readySet == null || readySet.Contains(p)) continue;
            
            OfflineTakeoverCore.EnqueueGhostAction(new ReadyToBeginEnemyTurnAction(p), p.NetId);
        }
    }
}

// ==========================================
// 战斗托管 (3/3)
// ==========================================
[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.SetReadyToEndTurn))]
public static class CombatTurnEndTakeoverPatch
{
    static bool Prepare() => OfflineTakeoverCore.IsTakeoverConfigEnabled();

    public static void Postfix(CombatManager __instance, Player player)
    {
        if (!OfflineTakeoverCore.IsDirectConnectActive) return;
        if (RunManager.Instance.NetService.Type != NetGameType.Host) return;

        var state = __instance.DebugOnlyGetState();
        if (state == null || !__instance.IsInProgress || state.CurrentSide != CombatSide.Player) return;
        if (state.Players.All(p => OfflineTakeoverCore.IsGhost(p.NetId))) return;

        var allOnlineReady = state.Players.Where(p => !OfflineTakeoverCore.IsGhost(p.NetId) && p.Creature.IsAlive).All(__instance.IsPlayerReadyToEndTurn);
        if (!allOnlineReady) return;

        var roundNumber = state.RoundNumber;
        foreach (var p in state.Players)
        {
            if (!OfflineTakeoverCore.IsGhost(p.NetId) || !p.Creature.IsAlive || __instance.IsPlayerReadyToEndTurn(p)) continue;
            OfflineTakeoverCore.EnqueueGhostAction(new EndPlayerTurnAction(p, roundNumber), p.NetId);
        }
    }
}

[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.SetReadyToBeginEnemyTurn))]
public static class SetReadyToBeginEnemyTakeoverPatch
{
    private static readonly FieldInfo? ReadyPlayersField = AccessTools.Field(typeof(CombatManager), "_playersReadyToBeginEnemyTurn");

    static bool Prepare() => OfflineTakeoverCore.IsTakeoverConfigEnabled();

    public static void Postfix(CombatManager __instance, Player player)
    {
        if (!OfflineTakeoverCore.IsDirectConnectActive) return;
        if (RunManager.Instance.NetService.Type != NetGameType.Host) return;

        if (ReadyPlayersField?.GetValue(__instance) is not HashSet<Player> readySet) return;
            
        var state = __instance.DebugOnlyGetState();
        if (state == null) return;
        if (state.Players.All(p => OfflineTakeoverCore.IsGhost(p.NetId))) return;

        var allOnlineReady = state.Players.Where(p => !OfflineTakeoverCore.IsGhost(p.NetId)).All(p => readySet.Contains(p));
        if (!allOnlineReady) return;
        
        foreach (var p in state.Players.Where(p => OfflineTakeoverCore.IsGhost(p.NetId)))
        {
            if (readySet.Contains(p)) continue;
            OfflineTakeoverCore.EnqueueGhostAction(new ReadyToBeginEnemyTurnAction(p), p.NetId);
        }
    }
}

// ==========================================
// 地图选择托管
// ==========================================
[HarmonyPatch(typeof(MapSelectionSynchronizer), nameof(MapSelectionSynchronizer.PlayerVotedForMapCoord))]
public static class MapSelectionGhostPatch
{
    private static readonly FieldInfo? VotesField = AccessTools.Field(typeof(MapSelectionSynchronizer), "_votes");
    private static readonly MethodInfo? MoveMethod = AccessTools.Method(typeof(MapSelectionSynchronizer), "MoveToMapCoord");

    static bool Prepare() => OfflineTakeoverCore.IsTakeoverConfigEnabled();

    public static void Postfix(MapSelectionSynchronizer __instance)
    {
        if (!OfflineTakeoverCore.IsDirectConnectActive) return;
        if (RunManager.Instance.NetService.Type != NetGameType.Host) return;
        if (VotesField?.GetValue(__instance) is not IList votesList) return;

        var players = RunManager.Instance.DebugOnlyGetState()?.Players;
        if (players == null) return;

        if (players.All(p => OfflineTakeoverCore.IsGhost(p.NetId))) return;

        MapVote? fallbackVote = null;
        var allOnlineVoted = true;

        for (var i = 0; i < players.Count; i++)
        {
            if (i >= votesList.Count) break;
            if (OfflineTakeoverCore.IsGhost(players[i].NetId)) continue;

            if (votesList[i] is not MapVote vote) allOnlineVoted = false;
            else fallbackVote = vote;
        }
        
        if (!allOnlineVoted || !fallbackVote.HasValue) return;
        
        var needsInvoke = false;
        for (var i = 0; i < votesList.Count; i++)
        {
            if ((votesList[i] as MapVote?).HasValue) continue;
            votesList[i] = fallbackVote;
            needsInvoke = true;
        }
        if (needsInvoke) MoveMethod?.Invoke(__instance, null);
    }
}

// ==========================================
// 遗物宝箱托管
// ==========================================
[HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.OnPicked))]
public static class TreasureUnblockPatch
{
    private static readonly FieldInfo? PlayerCollectionField = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_playerCollection");
    private static readonly FieldInfo? VotesField = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_votes");
    private static readonly FieldInfo? CurrentRelicsField = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_currentRelics");
    
    private static FieldInfo? _voteReceivedField;
    private static FieldInfo? _voteIndexField;

    static bool Prepare() => OfflineTakeoverCore.IsTakeoverConfigEnabled();

    public static void Postfix(TreasureRoomRelicSynchronizer __instance, Player player)
    {
        if (!OfflineTakeoverCore.IsDirectConnectActive) return;
        if (RunManager.Instance.NetService.Type != NetGameType.Host) return;

        var playerCollection = PlayerCollectionField?.GetValue(__instance) as IPlayerCollection;
        var votesList = VotesField?.GetValue(__instance) as IList;
        var currentRelics = CurrentRelicsField?.GetValue(__instance) as IEnumerable; 

        if (playerCollection == null || votesList == null || currentRelics == null) return;
        if (playerCollection.Players.All(p => OfflineTakeoverCore.IsGhost(p.NetId))) return;

        var relicCount = currentRelics.Cast<object>().Count();
        if (relicCount == 0) return;

        var usedIndices = new HashSet<int>();
        
        for (var i = 0; i < playerCollection.Players.Count; i++)
        {
            if (i >= votesList.Count) break;
            
            var pId = playerCollection.Players[i].NetId;
            if (OfflineTakeoverCore.IsGhost(pId)) continue;
            
            if (!HasVoted(votesList[i], out var vIndex)) return;
            if (vIndex.HasValue) usedIndices.Add(vIndex.Value);
        }

        for (var i = 0; i < playerCollection.Players.Count; i++)
        {
            if (i >= votesList.Count) break;

            var ghostPlayer = playerCollection.Players[i];
            if (!OfflineTakeoverCore.IsGhost(ghostPlayer.NetId) || HasVoted(votesList[i], out _)) continue;

            var pickIndex = -1;
            for (var j = 0; j < relicCount; j++)
            {
                if (usedIndices.Contains(j)) continue;
                pickIndex = j;
                usedIndices.Add(j);
                break;
            }

            if (pickIndex == -1) continue;
            GameAction pickAction;
            try 
            {
                pickAction = (GameAction)Activator.CreateInstance(typeof(PickRelicAction), ghostPlayer, (int?)pickIndex)!;
            }
            catch
            {
                pickAction = (GameAction)Activator.CreateInstance(typeof(PickRelicAction), ghostPlayer, pickIndex)!;
            }

            OfflineTakeoverCore.EnqueueGhostAction(pickAction, ghostPlayer.NetId);
        }

        return;

        bool HasVoted(object? voteObj, out int? votedIndex)
        {
            votedIndex = null;
            if (voteObj == null) return false;

            if (_voteReceivedField == null)
            {
                var type = voteObj.GetType();
                _voteReceivedField = AccessTools.Field(type, "voteReceived");
                _voteIndexField = AccessTools.Field(type, "index");
            }

            if (_voteReceivedField != null)
            {
                var received = (bool)_voteReceivedField.GetValue(voteObj)!;
                votedIndex = _voteIndexField?.GetValue(voteObj) as int?;
                return received;
            }

            if (voteObj is not int intVote) return false;
            votedIndex = intVote;
            return true;

        }
    }
}

// ==========================================
// 关卡(章节)跳转托管
// ==========================================
[HarmonyPatch(typeof(ActChangeSynchronizer), nameof(ActChangeSynchronizer.OnPlayerReady))]
public static class ActChangeGhostPatch
{
    private static readonly FieldInfo? RunStateField = AccessTools.Field(typeof(ActChangeSynchronizer), "_runState");
    private static readonly FieldInfo? ReadyPlayersField = AccessTools.Field(typeof(ActChangeSynchronizer), "_readyPlayers");

    static bool Prepare() => OfflineTakeoverCore.IsTakeoverConfigEnabled();

    public static void Postfix(ActChangeSynchronizer __instance, Player player)
    {
        if (!OfflineTakeoverCore.IsDirectConnectActive) return;
        if (RunManager.Instance.NetService.Type != NetGameType.Host) return;
        if (RunStateField?.GetValue(__instance) is not RunState state) return;
        if (state.Players.All(p => OfflineTakeoverCore.IsGhost(p.NetId))) return;
        if (ReadyPlayersField?.GetValue(__instance) is not IList readyPlayers) return;

        var allOnlineReady = !state.Players.Where((t, i) => !OfflineTakeoverCore.IsGhost(t.NetId) && (i >= readyPlayers.Count || !(bool)readyPlayers[i]!)).Any();
        if (!allOnlineReady) return;

        for (var i = 0; i < state.Players.Count; i++)
        {
            if (i >= readyPlayers.Count) break;
            
            var ghostPlayer = state.Players[i];
            if (!OfflineTakeoverCore.IsGhost(ghostPlayer.NetId) || (bool)readyPlayers[i]!) continue;
            
            OfflineTakeoverCore.EnqueueGhostAction(new VoteToMoveToNextActAction(ghostPlayer), ghostPlayer.NetId);
        }
    }
}

// ==========================================
// 战斗底层序列化状态同步托管
// ==========================================
[HarmonyPatch(typeof(CombatStateSynchronizer), nameof(CombatStateSynchronizer.StartSync))]
public static class CombatSyncTakeoverPatch
{
    private static readonly FieldInfo? NetServiceField = AccessTools.Field(typeof(CombatStateSynchronizer), "_netService");
    private static readonly FieldInfo? RunStateField = AccessTools.Field(typeof(CombatStateSynchronizer), "_runState");
    private static readonly FieldInfo? SyncDataField = AccessTools.Field(typeof(CombatStateSynchronizer), "_syncData");
    private static readonly MethodInfo? CheckSyncMethod = AccessTools.Method(typeof(CombatStateSynchronizer), "CheckSyncCompleted");

    static bool Prepare() => OfflineTakeoverCore.IsTakeoverConfigEnabled();

    public static void Postfix(CombatStateSynchronizer __instance)
    {
        if (!OfflineTakeoverCore.IsDirectConnectActive) return;
        if (NetServiceField?.GetValue(__instance) is not INetGameService { Type: NetGameType.Host } netService) return;

        var runState = RunStateField?.GetValue(__instance) as RunState;
        var syncData = SyncDataField?.GetValue(__instance) as Dictionary<ulong, SerializablePlayer>;
        if (runState == null || syncData == null) return;

        if (runState.Players.All(p => OfflineTakeoverCore.IsGhost(p.NetId))) return;

        foreach (var ghostPlayer in runState.Players)
        {
            if (!OfflineTakeoverCore.IsGhost(ghostPlayer.NetId)) continue;
            
            var serializedGhost = ghostPlayer.ToSerializable();
            syncData[ghostPlayer.NetId] = serializedGhost;
            var message = new SyncPlayerDataMessage { player = serializedGhost };
            netService.SendMessage(message);
        }

        try
        {
            CheckSyncMethod?.Invoke(__instance, null);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
        {
        }
    }
}

[HarmonyPatch(typeof(CombatStateSynchronizer), "OnSyncPlayerMessageReceived")]
public static class CombatSyncReceiveTakeoverPatch
{
    private static readonly FieldInfo? SyncDataField = AccessTools.Field(typeof(CombatStateSynchronizer), "_syncData");
    private static readonly MethodInfo? CheckSyncMethod = AccessTools.Method(typeof(CombatStateSynchronizer), "CheckSyncCompleted");

    static bool Prepare() => OfflineTakeoverCore.IsTakeoverConfigEnabled();

    public static bool Prefix(CombatStateSynchronizer __instance, SyncPlayerDataMessage syncMessage, ulong senderId)
    {
        if (!OfflineTakeoverCore.IsTakeoverConfigEnabled()) return true;
        if (!OfflineTakeoverCore.IsDirectConnectActive) return true;
        if (RunManager.Instance.NetService.Type != NetGameType.Client) return true;
        
        var realPlayerId = syncMessage.player.NetId;
        if (realPlayerId == senderId) return true;
        if (!OfflineTakeoverCore.IsGhost(realPlayerId)) return true;
        if (SyncDataField?.GetValue(__instance) is not Dictionary<ulong, SerializablePlayer> syncData) return false;
        
        syncData[realPlayerId] = syncMessage.player;
        
        try 
        {
            CheckSyncMethod?.Invoke(__instance, null);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
        {
        }
        
        return false;
    }
}

// ==========================================
// 事件房间托管
// ==========================================
[HarmonyPatch(typeof(EventSynchronizer), "ChooseOptionForEvent")]
[HarmonyPatch(typeof(EventSynchronizer), "PlayerVotedForSharedOptionIndex")]
public static class EventUnblockPatch
{
    private static readonly FieldInfo? PlayerCollectionField = AccessTools.Field(typeof(EventSynchronizer), "_playerCollection");
    private static readonly PropertyInfo? IsSharedProperty = AccessTools.Property(typeof(EventSynchronizer), "IsShared");
    private static readonly FieldInfo? PlayerVotesField = AccessTools.Field(typeof(EventSynchronizer), "_playerVotes");
    private static readonly FieldInfo? PageIndexField = AccessTools.Field(typeof(EventSynchronizer), "_pageIndex");
    private static readonly MethodInfo? VoteMethod = AccessTools.Method(typeof(EventSynchronizer), "PlayerVotedForSharedOptionIndex");
    private static readonly FieldInfo? EventsField = AccessTools.Field(typeof(EventSynchronizer), "_events");
    private static readonly MethodInfo? ChooseMethod = AccessTools.Method(typeof(EventSynchronizer), "ChooseOptionForEvent");

    static bool Prepare() => OfflineTakeoverCore.IsTakeoverConfigEnabled();

    public static void Postfix(EventSynchronizer __instance)
    {
        if (!OfflineTakeoverCore.IsDirectConnectActive) return;
        if (RunManager.Instance.NetService.Type != NetGameType.Host) return;

        var playerCollection = PlayerCollectionField?.GetValue(__instance) as IPlayerCollection;
        var isShared = IsSharedProperty != null && (bool)IsSharedProperty.GetValue(__instance)!;

        if (playerCollection == null) return;
        if (playerCollection.Players.All(p => OfflineTakeoverCore.IsGhost(p.NetId))) return;

        if (isShared)
        {
            var playerVotesObj = PlayerVotesField?.GetValue(__instance);
            if (playerVotesObj is not IList playerVotes) return;

            var allOnlineVoted = true;
            uint? fallbackVote = null;

            for (var i = 0; i < playerCollection.Players.Count; i++)
            {
                if (i >= playerVotes.Count) break;
                if (OfflineTakeoverCore.IsGhost(playerCollection.Players[i].NetId)) continue;

                if (playerVotes[i] is not uint vote) { allOnlineVoted = false; break; }
                fallbackVote = vote;
            }

            if (!allOnlineVoted || !fallbackVote.HasValue) return;
            for (var i = 0; i < playerCollection.Players.Count; i++)
            {
                if (i >= playerVotes.Count) break;
                if (!OfflineTakeoverCore.IsGhost(playerCollection.Players[i].NetId) || (playerVotes[i] as uint?).HasValue)
                    continue;
                
                var pageIndex = (uint)(PageIndexField?.GetValue(__instance) ?? 0U);
                VoteMethod?.Invoke(__instance, [playerCollection.Players[i], fallbackVote.Value, pageIndex]);
            }
        }
        else
        {
            if (EventsField?.GetValue(__instance) is not List<EventModel> events) return;

            var allOnlineFinished = !playerCollection.Players.Where((t, i) => !OfflineTakeoverCore.IsGhost(t.NetId) && !events[i].IsFinished).Any();
            if (!allOnlineFinished) return;
            
            for (var i = 0; i < playerCollection.Players.Count; i++)
            {
                var ghostPlayer = playerCollection.Players[i];
                if (!OfflineTakeoverCore.IsGhost(ghostPlayer.NetId) || events[i].IsFinished) continue;
                
                var safeGuard = 0; 
                while (events.Count > i && !events[i].IsFinished && events[i].CurrentOptions.Count > 0 && safeGuard < 5)
                {
                    ChooseMethod?.Invoke(__instance, [ghostPlayer, 0]);
                    safeGuard++;
                }
            }
        }
    }
}

// ==========================================
// 泛用交互托管
// ==========================================
[HarmonyPatch(typeof(PlayerChoiceSynchronizer), nameof(PlayerChoiceSynchronizer.WaitForRemoteChoice))]
public static class AutoPassRemoteChoiceForGhostsPatch
{
    static bool Prepare() => OfflineTakeoverCore.IsTakeoverConfigEnabled();

    public static void Prefix(PlayerChoiceSynchronizer __instance, Player player, uint choiceId)
    {
        if (!OfflineTakeoverCore.IsDirectConnectActive) return;
        if (RunManager.Instance.NetService.Type != NetGameType.Host) return;
        if (!OfflineTakeoverCore.IsGhost(player.NetId)) return;

        var defaultNetResult = PlayerChoiceResult.FromIndex(0).ToNetData();
        __instance.ReceiveReplayChoice(player, choiceId, defaultNetResult);
    }
}

// ==========================================
// 双开测试冲突拦截
// ==========================================
[HarmonyPatch(typeof(GodotFileIo), nameof(GodotFileIo.RenameFile))]
public static class LocalTestingRenameFilePatch
{
    static bool Prepare() => OfflineTakeoverCore.IsTakeoverConfigEnabled();

    public static Exception? Finalizer(Exception? __exception)
    {
        if (!OfflineTakeoverCore.IsDirectConnectActive || __exception == null || !__exception.GetType().Name.Contains("SaveException")) return __exception;
        
        Log.Warn($"[DirectConnectIP] 拦截了双开重命名冲突");
        return null;
    }
}

[HarmonyPatch(typeof(GodotFileIo), nameof(GodotFileIo.WriteFile), typeof(string), typeof(byte[]))]
public static class LocalTestingWriteFileBytesPatch
{
    static bool Prepare() => OfflineTakeoverCore.IsTakeoverConfigEnabled();

    public static Exception? Finalizer(Exception? __exception)
    {
        if (!OfflineTakeoverCore.IsDirectConnectActive || __exception == null || !__exception.GetType().Name.Contains("SaveException")) return __exception;
        
        Log.Warn($"[DirectConnectIP] 拦截了双开字节写入冲突");
        return null;
    }
}

[HarmonyPatch(typeof(GodotFileIo), nameof(GodotFileIo.WriteFile), typeof(string), typeof(string))]
public static class LocalTestingWriteFileStringPatch
{
    static bool Prepare() => OfflineTakeoverCore.IsTakeoverConfigEnabled();

    public static Exception? Finalizer(Exception? __exception)
    {
        if (!OfflineTakeoverCore.IsDirectConnectActive || __exception == null || !__exception.GetType().Name.Contains("SaveException")) return __exception;
        
        Log.Warn($"[DirectConnectIP] 拦截了双开文本写入冲突");
        return null;
    }
}
