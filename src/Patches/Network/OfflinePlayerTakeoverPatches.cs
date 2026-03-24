using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace DirectConnectIP.Patches.Network;

[HarmonyPatch(typeof(PlayCardAction), "ExecuteAction")]
public static class PlayCardActionGhostPatch
{
    static bool Prepare() => OfflineTakeoverUtility.IsTakeoverEnabled();

    public static void Prefix(PlayCardAction __instance, out IDisposable __state)
    {
        __state = null;
        if (OfflineTakeoverUtility.IsGhost(__instance.OwnerId))
        {
            __state = CardSelectCmd.PushSelector(new VakuuCardSelector());
        }
    }

    public static void Postfix(Task __result, IDisposable __state)
    {
        if (__state != null && __result != null)
        {
            // 保证卡牌的异步效果（包括弹窗动画等）彻底走完后，才卸下护盾
            __result.ContinueWith(_ => __state.Dispose());
        }
        else
        {
            __state?.Dispose();
        }
    }
}

// ==========================================
// 后台并发出牌
// ==========================================
[HarmonyPatch(typeof(ActionQueueSynchronizer), nameof(ActionQueueSynchronizer.SetCombatState))]
public static class ConcurrentAutoPlayTurnPatch
{
    static bool Prepare() => OfflineTakeoverUtility.IsTakeoverEnabled();

    public static void Postfix(ActionQueueSynchronizer __instance, ActionSynchronizerCombatState combatState)
    {
        if (RunManager.Instance.NetService.Type != NetGameType.Host) return;
        if (combatState != ActionSynchronizerCombatState.PlayPhase) return;
        
        var state = CombatManager.Instance.DebugOnlyGetState();
        if (state == null) return;

        var roundNumber = state.RoundNumber;

        foreach (var p in state.Players)
        {
            if (!OfflineTakeoverUtility.IsGhost(p.NetId) || !p.Creature.IsAlive) continue;
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
                var combatState = ghostPlayer.Creature.CombatState;
                if (combatState == null || combatState.RoundNumber != roundNumber) break;
                if (CombatManager.Instance.IsPlayerReadyToEndTurn(ghostPlayer)) break;

                var handPile = PileType.Hand.GetPile(ghostPlayer);
                
                var playableCards = handPile.Cards.Where(c => c.CanPlay(out _, out _)).ToList();
                var cardToPlay = playableCards.FirstOrDefault(c => !pendingCards.Contains(c));

                if (cardToPlay != null)
                {
                    Creature target;
                    switch (cardToPlay.TargetType)
                    {
                        case TargetType.AnyEnemy:
                            target = combatState.HittableEnemies.FirstOrDefault();
                            break;
                        case TargetType.AnyPlayer:
                            target = ghostPlayer.Creature;
                            break;
                        case TargetType.AnyAlly:
                            target = combatState.Allies.FirstOrDefault(c => c is { IsAlive: true, IsPlayer: true } && c != ghostPlayer.Creature);
                            break;
                        default:
                            target = null; 
                            break;
                    }

                    Log.Info($"[DirectConnectIP] 幽灵并发 AI 生成出牌动作: {cardToPlay.Id.Entry}");
                    pendingCards.Add(cardToPlay);
                    var playAction = new PlayCardAction(cardToPlay, target);
                    OfflineTakeoverUtility.EnqueueGhostAction(playAction, ghostPlayer.NetId);

                    // 稍作停顿再看下一张牌，让出牌有节奏感
                    await Task.Delay(1200); 
                }
                else if (playableCards.Count > 0)
                {
                    // 手里还有能打的牌，但全都已经被发包排队了，等待底层动画播完、手牌刷新
                    await Task.Delay(800);
                }
                else
                {
                    Log.Info($"[DirectConnectIP] 幽灵玩家 {ghostPlayer.NetId} 无牌可出，提交回合结束指令。");
                    OfflineTakeoverUtility.EnqueueGhostAction(new EndPlayerTurnAction(ghostPlayer, roundNumber), ghostPlayer.NetId);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[DirectConnectIP] 幽灵后台 AI 发生异常: {ex}");
            if (!CombatManager.Instance.IsPlayerReadyToEndTurn(ghostPlayer))
            {
                OfflineTakeoverUtility.EnqueueGhostAction(new EndPlayerTurnAction(ghostPlayer, roundNumber), ghostPlayer.NetId);
            }
        }
    }
}

// ==========================================
// 战斗托管 (2/2)
// ==========================================
[HarmonyPatch(typeof(ActionQueueSynchronizer), nameof(ActionQueueSynchronizer.SetCombatState))]
public static class AutoReadyEnemyTurnPatch
{
    static bool Prepare() => OfflineTakeoverUtility.IsTakeoverEnabled();

    public static void Postfix(ActionQueueSynchronizer __instance, ActionSynchronizerCombatState combatState)
    {
        if (RunManager.Instance.NetService.Type != NetGameType.Host) return;
        
        if (combatState != ActionSynchronizerCombatState.EndTurnPhaseOne) return;
        
        var state = CombatManager.Instance.DebugOnlyGetState();
        if (state == null) return;

        var readySet = (HashSet<Player>)AccessTools.Field(typeof(CombatManager), "_playersReadyToBeginEnemyTurn")?.GetValue(CombatManager.Instance);
        
        foreach (var p in state.Players)
        {
            if (!OfflineTakeoverUtility.IsGhost(p.NetId) || !p.Creature.IsAlive) continue;
            if (readySet == null || readySet.Contains(p)) continue;
            Log.Info($"[DirectConnectIP] 队列清空完毕，为主机托管的幽灵 {p.NetId} 发送迎击指令。");
            OfflineTakeoverUtility.EnqueueGhostAction(new ReadyToBeginEnemyTurnAction(p), p.NetId);
        }
    }
}

// ==========================================
// 战斗托管 (3/3)
// ==========================================
[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.SetReadyToEndTurn))]
public static class CombatTurnEndTakeoverPatch
{
    static bool Prepare() => OfflineTakeoverUtility.IsTakeoverEnabled();

    public static void Postfix(CombatManager __instance, Player player)
    {
        if (RunManager.Instance.NetService.Type != NetGameType.Host) return;

        var state = __instance.DebugOnlyGetState();
        if (state == null || !__instance.IsInProgress || state.CurrentSide != CombatSide.Player) return;
        if (state.Players.All(p => OfflineTakeoverUtility.IsGhost(p.NetId))) return;

        var allOnlineReady = state.Players.Where(p => !OfflineTakeoverUtility.IsGhost(p.NetId) && p.Creature.IsAlive).All(__instance.IsPlayerReadyToEndTurn);
        if (!allOnlineReady) return;

        var roundNumber = state.RoundNumber;
        foreach (var p in state.Players)
        {
            if (!OfflineTakeoverUtility.IsGhost(p.NetId) || !p.Creature.IsAlive || __instance.IsPlayerReadyToEndTurn(p)) continue;
            OfflineTakeoverUtility.EnqueueGhostAction(new EndPlayerTurnAction(p, roundNumber), p.NetId);
        }
    }
}

[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.SetReadyToBeginEnemyTurn))]
public static class SetReadyToBeginEnemyTakeoverPatch
{
    static bool Prepare() => OfflineTakeoverUtility.IsTakeoverEnabled();

    public static void Postfix(CombatManager __instance, Player player)
    {
        if (RunManager.Instance.NetService.Type != NetGameType.Host) return;

        var readySet = (HashSet<Player>)AccessTools.Field(typeof(CombatManager), "_playersReadyToBeginEnemyTurn")?.GetValue(__instance);
        if (readySet == null) return;
            
        var state = __instance.DebugOnlyGetState();
        if (state == null) return;
        if (state.Players.All(p => OfflineTakeoverUtility.IsGhost(p.NetId))) return;

        var allOnlineReady = state.Players.Where(p => !OfflineTakeoverUtility.IsGhost(p.NetId)).All(p => readySet.Contains(p));
        if (!allOnlineReady) return;
        
        foreach (var p in state.Players.Where(p => OfflineTakeoverUtility.IsGhost(p.NetId)))
        {
            if (readySet.Contains(p)) continue;
            OfflineTakeoverUtility.EnqueueGhostAction(new ReadyToBeginEnemyTurnAction(p), p.NetId);
        }
    }
}

// ==========================================
// 地图选择托管
// ==========================================
[HarmonyPatch(typeof(MapSelectionSynchronizer), nameof(MapSelectionSynchronizer.PlayerVotedForMapCoord))]
public static class MapSelectionGhostPatch
{
    static bool Prepare() => OfflineTakeoverUtility.IsTakeoverEnabled();

    public static void Postfix(MapSelectionSynchronizer __instance)
    {
        if (RunManager.Instance.NetService.Type != NetGameType.Host) return;

        var votesList = AccessTools.Field(typeof(MapSelectionSynchronizer), "_votes")?.GetValue(__instance) as IList;
        if (votesList == null) return;

        var players = RunManager.Instance.DebugOnlyGetState()?.Players;
        if (players == null) return;

        if (players.All(p => OfflineTakeoverUtility.IsGhost(p.NetId))) return;

        MapVote? fallbackVote = null;
        var allOnlineVoted = true;

        for (var i = 0; i < players.Count; i++)
        {
            if (i >= votesList.Count) break;
            if (OfflineTakeoverUtility.IsGhost(players[i].NetId)) continue;

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
        if (needsInvoke) AccessTools.Method(typeof(MapSelectionSynchronizer), "MoveToMapCoord")?.Invoke(__instance, null);
    }
}

// ==========================================
// 遗物宝箱托管
// ==========================================
[HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.OnPicked))]
public static class TreasureUnblockPatch
{
    static bool Prepare() => OfflineTakeoverUtility.IsTakeoverEnabled();

    public static void Postfix(TreasureRoomRelicSynchronizer __instance, Player player)
    {
        if (RunManager.Instance.NetService.Type != NetGameType.Host) return;

        var playerCollectionField = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_playerCollection");
        var votesField = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_votes");
        var currentRelicsField = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_currentRelics");

        var playerCollection = playerCollectionField?.GetValue(__instance) as IPlayerCollection;
        var votesList = votesField?.GetValue(__instance) as IList;
        var currentRelics = currentRelicsField?.GetValue(__instance) as IEnumerable; 

        if (playerCollection == null || votesList == null || currentRelics == null) return;
        if (playerCollection.Players.All(p => OfflineTakeoverUtility.IsGhost(p.NetId))) return;

        var relicCount = currentRelics.Cast<object>().Count();
        if (relicCount == 0) return;

        var usedIndices = new HashSet<int>();
        
        for (var i = 0; i < playerCollection.Players.Count; i++)
        {
            if (i >= votesList.Count) break;
            
            var pId = playerCollection.Players[i].NetId;
            if (OfflineTakeoverUtility.IsGhost(pId)) continue;
            
            if (!HasVoted(votesList[i], out var vIndex)) return;
            if (vIndex.HasValue) usedIndices.Add(vIndex.Value);
        }

        for (var i = 0; i < playerCollection.Players.Count; i++)
        {
            if (i >= votesList.Count) break;

            var ghostPlayer = playerCollection.Players[i];
            if (!OfflineTakeoverUtility.IsGhost(ghostPlayer.NetId) || HasVoted(votesList[i], out _)) continue;

            var pickIndex = 0;
            for (var j = 0; j < relicCount; j++)
            {
                if (usedIndices.Contains(j)) continue;
                pickIndex = j;
                usedIndices.Add(j);
                break;
            }

            GameAction pickAction;
            try 
            {
                pickAction = (GameAction)Activator.CreateInstance(typeof(PickRelicAction), ghostPlayer, (int?)pickIndex);
            }
            catch
            {
                pickAction = (GameAction)Activator.CreateInstance(typeof(PickRelicAction), ghostPlayer, pickIndex);
            }

            if (pickAction != null) OfflineTakeoverUtility.EnqueueGhostAction(pickAction, ghostPlayer.NetId);
        }

        return;

        bool HasVoted(object voteObj, out int? votedIndex)
        {
            votedIndex = null;
            if (voteObj == null) return false;

            var type = voteObj.GetType();

            var voteReceivedField = AccessTools.Field(type, "voteReceived");
            if (voteReceivedField != null)
            {
                var indexField = AccessTools.Field(type, "index");
                var received = (bool)voteReceivedField.GetValue(voteObj)!;
                votedIndex = indexField?.GetValue(voteObj) as int?;
                return received;
            }

            if (type != typeof(int)) return false;
            votedIndex = (int)voteObj;
            return true;

        }
    }
}

[HarmonyPatch(typeof(NHandImageCollection), "UpdateHandVisibility")]
public static class TreasureHandExceptionHandlerPatch
{
    static bool Prepare() => OfflineTakeoverUtility.IsTakeoverEnabled();

    public static Exception Finalizer(Exception __exception)
    {
        switch (__exception)
        {
            case null:
            case InvalidOperationException when __exception.Message.Contains("PeerInputState"):
                return null;
            default:
                return __exception;
        }
    }
}

// ==========================================
// 关卡(章节)跳转托管
// ==========================================
[HarmonyPatch(typeof(ActChangeSynchronizer), nameof(ActChangeSynchronizer.OnPlayerReady))]
public static class ActChangeGhostPatch
{
    static bool Prepare() => OfflineTakeoverUtility.IsTakeoverEnabled();

    public static void Postfix(ActChangeSynchronizer __instance, Player player)
    {
        if (RunManager.Instance.NetService.Type != NetGameType.Host) return;

        var stateField = AccessTools.Field(typeof(ActChangeSynchronizer), "_runState");
        if (stateField?.GetValue(__instance) is not RunState state) return;

        if (state.Players.All(p => OfflineTakeoverUtility.IsGhost(p.NetId))) return;

        var readyPlayersField = AccessTools.Field(typeof(ActChangeSynchronizer), "_readyPlayers");
        if (readyPlayersField?.GetValue(__instance) is not IList readyPlayers) return;

        var allOnlineReady = !state.Players.Where((t, i) => !OfflineTakeoverUtility.IsGhost(t.NetId) && (i >= readyPlayers.Count || !(bool)readyPlayers[i]!)).Any();

        if (!allOnlineReady) return;

        for (var i = 0; i < state.Players.Count; i++)
        {
            if (i >= readyPlayers.Count) break;
            
            var ghostPlayer = state.Players[i];
            if (!OfflineTakeoverUtility.IsGhost(ghostPlayer.NetId) || (bool)readyPlayers[i]) continue;
            
            Log.Info($"[DirectConnectIP] 真人已确认前往下一章，为幽灵 {ghostPlayer.NetId} 自动发送跳转指令。");
            OfflineTakeoverUtility.EnqueueGhostAction(new VoteToMoveToNextActAction(ghostPlayer), ghostPlayer.NetId);
        }
    }
}

// ==========================================
// 战斗底层序列化状态同步托管
// ==========================================
[HarmonyPatch(typeof(CombatStateSynchronizer), nameof(CombatStateSynchronizer.StartSync))]
public static class CombatSyncTakeoverPatch
{
    static bool Prepare() => OfflineTakeoverUtility.IsTakeoverEnabled();

    public static void Postfix(CombatStateSynchronizer __instance)
    {
        var netService = (INetGameService)AccessTools.Field(typeof(CombatStateSynchronizer), "_netService")?.GetValue(__instance);
        if (netService is not { Type: NetGameType.Host }) return;

        var runState = (RunState)AccessTools.Field(typeof(CombatStateSynchronizer), "_runState")?.GetValue(__instance);
        var syncData = (Dictionary<ulong, SerializablePlayer>)AccessTools.Field(typeof(CombatStateSynchronizer), "_syncData")?.GetValue(__instance);
        if (runState == null || syncData == null) return;

        if (runState.Players.All(p => OfflineTakeoverUtility.IsGhost(p.NetId))) return;

        foreach (var ghostPlayer in runState.Players)
        {
            if (!OfflineTakeoverUtility.IsGhost(ghostPlayer.NetId)) continue;
            
            var serializedGhost = ghostPlayer.ToSerializable();
            syncData[ghostPlayer.NetId] = serializedGhost;
            var message = new SyncPlayerDataMessage { player = serializedGhost };
            netService.SendMessage(message);
        }

        try
        {
            AccessTools.Method(typeof(CombatStateSynchronizer), "CheckSyncCompleted")?.Invoke(__instance, null);
        }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
        {
        }
    }
}

[HarmonyPatch(typeof(CombatStateSynchronizer), "OnSyncPlayerMessageReceived")]
public static class CombatSyncReceiveTakeoverPatch
{
    static bool Prepare() => OfflineTakeoverUtility.IsTakeoverEnabled();

    public static bool Prefix(CombatStateSynchronizer __instance, SyncPlayerDataMessage syncMessage, ulong senderId)
    {
        var realPlayerId = syncMessage.player.NetId;

        if (realPlayerId == senderId) return true;
        
        var syncData = (Dictionary<ulong, SerializablePlayer>)AccessTools.Field(typeof(CombatStateSynchronizer), "_syncData")?.GetValue(__instance);
        if (syncData == null) return false;
        
        syncData[realPlayerId] = syncMessage.player;
        
        try 
        {
            AccessTools.Method(typeof(CombatStateSynchronizer), "CheckSyncCompleted")?.Invoke(__instance, null);
        }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
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
    static bool Prepare() => OfflineTakeoverUtility.IsTakeoverEnabled();

    public static void Postfix(EventSynchronizer __instance)
    {
        if (RunManager.Instance.NetService.Type != NetGameType.Host) return;

        var playerCollectionField = AccessTools.Field(typeof(EventSynchronizer), "_playerCollection");
        var isSharedProperty = AccessTools.Property(typeof(EventSynchronizer), "IsShared");
        var playerVotesField = AccessTools.Field(typeof(EventSynchronizer), "_playerVotes");

        var playerCollection = playerCollectionField?.GetValue(__instance) as IPlayerCollection;
        var isShared = isSharedProperty != null && (bool)isSharedProperty.GetValue(__instance)!;

        if (playerCollection == null) return;
        if (playerCollection.Players.All(p => OfflineTakeoverUtility.IsGhost(p.NetId))) return;

        if (isShared)
        {
            var playerVotesObj = playerVotesField?.GetValue(__instance);
            if (playerVotesObj is not IList playerVotes) return;

            var allOnlineVoted = true;
            uint? fallbackVote = null;

            for (var i = 0; i < playerCollection.Players.Count; i++)
            {
                if (i >= playerVotes.Count) break;
                if (OfflineTakeoverUtility.IsGhost(playerCollection.Players[i].NetId)) continue;

                if (playerVotes[i] is not uint vote) { allOnlineVoted = false; break; }
                fallbackVote = vote;
            }

            if (!allOnlineVoted || !fallbackVote.HasValue) return;
            for (var i = 0; i < playerCollection.Players.Count; i++)
            {
                if (i >= playerVotes.Count) break;
                if (!OfflineTakeoverUtility.IsGhost(playerCollection.Players[i].NetId) || (playerVotes[i] as uint?).HasValue)
                    continue;
                
                var pageIndexField = AccessTools.Field(typeof(EventSynchronizer), "_pageIndex");
                var pageIndex = (uint)(pageIndexField?.GetValue(__instance) ?? 0U);

                var voteMethod = AccessTools.Method(typeof(EventSynchronizer), "PlayerVotedForSharedOptionIndex");
                voteMethod?.Invoke(__instance, [playerCollection.Players[i], fallbackVote.Value, pageIndex]);
            }
        }
        else
        {
            var eventsField = AccessTools.Field(typeof(EventSynchronizer), "_events");
            if (eventsField?.GetValue(__instance) is not List<EventModel> events) return;

            var allOnlineFinished = !playerCollection.Players.Where((t, i) => !OfflineTakeoverUtility.IsGhost(t.NetId) && !events[i].IsFinished).Any();
            if (!allOnlineFinished) return;
            
            for (var i = 0; i < playerCollection.Players.Count; i++)
            {
                var ghostPlayer = playerCollection.Players[i];
                if (!OfflineTakeoverUtility.IsGhost(ghostPlayer.NetId) || events[i].IsFinished) continue;
                
                var chooseMethod = AccessTools.Method(typeof(EventSynchronizer), "ChooseOptionForEvent");
                var safeGuard = 0; 
                
                while (events.Count > i && !events[i].IsFinished && events[i].CurrentOptions.Count > 0 && safeGuard < 5)
                {
                    Log.Info($"[DirectConnectIP] 正在为离线玩家 {ghostPlayer.NetId} 推进事件选项...");
                    chooseMethod?.Invoke(__instance, [ghostPlayer, 0]);
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
    static bool Prepare() => OfflineTakeoverUtility.IsTakeoverEnabled();

    public static void Prefix(PlayerChoiceSynchronizer __instance, Player player, uint choiceId)
    {
        if (!OfflineTakeoverUtility.IsGhost(player.NetId)) return;

        Log.Info($"[DirectConnectIP] 侦测到等待离线玩家 {player.NetId} 的选择，注入默认选项...");

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
    static bool Prepare() => OfflineTakeoverUtility.IsTakeoverEnabled();

    public static Exception Finalizer(Exception __exception)
    {
        if (__exception == null || !__exception.GetType().Name.Contains("SaveException")) return __exception;
        Log.Warn($"[DirectConnectIP] 拦截了双开重命名冲突");
        return null;
    }
}

[HarmonyPatch(typeof(GodotFileIo), nameof(GodotFileIo.WriteFile), typeof(string), typeof(byte[]))]
public static class LocalTestingWriteFileBytesPatch
{
    static bool Prepare() => OfflineTakeoverUtility.IsTakeoverEnabled();

    public static Exception Finalizer(Exception __exception)
    {
        if (__exception == null || !__exception.GetType().Name.Contains("SaveException")) return __exception;
        Log.Warn($"[DirectConnectIP] 拦截了双开字节写入冲突");
        return null;
    }
}

[HarmonyPatch(typeof(GodotFileIo), nameof(GodotFileIo.WriteFile), typeof(string), typeof(string))]
public static class LocalTestingWriteFileStringPatch
{
    static bool Prepare() => OfflineTakeoverUtility.IsTakeoverEnabled();

    public static Exception Finalizer(Exception __exception)
    {
        if (__exception == null || !__exception.GetType().Name.Contains("SaveException")) return __exception;
        Log.Warn($"[DirectConnectIP] 拦截了双开文本写入冲突");
        return null;
    }
}