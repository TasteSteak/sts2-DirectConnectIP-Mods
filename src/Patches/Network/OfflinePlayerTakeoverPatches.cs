using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace DirectConnectIP.Patches.Network;

[HarmonyPatch]
public static class OfflinePlayerTakeoverPatches
{
    static bool Prepare()
    {
        return ModEntry.Config.EnableOfflineTakeover;
    }
    
    private static HashSet<ulong> GetOnlineIds()
    {
        var ids = new HashSet<ulong> { ModEntry.Config.LocalPlayerId };
        var netService = RunManager.Instance.NetService;
                                                                                                                                                                                                      
        if (!netService.IsConnected || netService is not NetHostGameService hostService) return ids;
        
        foreach (var peer in hostService.ConnectedPeers)
        {
            ids.Add(peer.peerId);
        }

        return ids;
    }

    private static void EnqueueGhostAction(GameAction action, ulong ghostNetId)
    {
        var sync = RunManager.Instance.ActionQueueSynchronizer;
        var enqueueMethod = AccessTools.Method(typeof(ActionQueueSynchronizer), "EnqueueAction");
        if (enqueueMethod != null)
        {
            enqueueMethod.Invoke(sync, [action, ghostNetId]);
        }
        else
        {
            Log.Error("[DirectConnectIP] 找不到 EnqueueAction 方法，代管发包失败！");
        }
    }

    private static void EnqueueHostOnlyAction(GameAction action)
    {
        try
        {
            var sync = RunManager.Instance.ActionQueueSynchronizer;
            var queueSet = AccessTools.Field(typeof(ActionQueueSynchronizer), "_actionQueueSet")?.GetValue(sync);
            if (queueSet != null)
            {
                AccessTools.Method(queueSet.GetType(), "EnqueueWithoutSynchronizing")?.Invoke(queueSet, [action]);
            }
            else
            {
                Log.Error("[DirectConnectIP] 找不到 _actionQueueSet，HostOnly 代管动作发包失败！");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[DirectConnectIP] EnqueueHostOnlyAction 出错: {ex}");
        }
    }

    // ==========================================
    // 战斗托管
    // ==========================================
    [HarmonyPatch(typeof(ActionQueueSynchronizer), nameof(ActionQueueSynchronizer.SetCombatState))]
    public static class AutoPassOnTurnStartPatch
    {
        public static void Postfix(ActionQueueSynchronizer __instance, ActionSynchronizerCombatState combatState)
        {
            if (RunManager.Instance.NetService.Type != NetGameType.Host) return;

            if (combatState != ActionSynchronizerCombatState.PlayPhase) return;
            
            var state = CombatManager.Instance.DebugOnlyGetState();
            if (state == null) return;

            var onlineIds = GetOnlineIds();
            if (state.Players.All(p => onlineIds.Contains(p.NetId))) return;

            var roundNumber = state.RoundNumber;

            foreach (var p in state.Players)
            {
                if (onlineIds.Contains(p.NetId) || !p.Creature.IsAlive) continue;
                
                var aiAction = new GhostAutoPlayTurnAction(p, roundNumber);
                EnqueueHostOnlyAction(aiAction);
            }
        }
    }
 
    // ==========================================
    // 地图选择托管
    // ==========================================
    [HarmonyPatch(typeof(MapSelectionSynchronizer), nameof(MapSelectionSynchronizer.PlayerVotedForMapCoord))]
    public static class MapSelectionGhostPatch
    {
        public static void Postfix(MapSelectionSynchronizer __instance)
        {
            if (RunManager.Instance.NetService.Type != NetGameType.Host) return;

            var votes = (List<MapVote?>)AccessTools.Field(typeof(MapSelectionSynchronizer), "_votes")?.GetValue(__instance);
            if (votes == null) return;

            var players = RunManager.Instance.DebugOnlyGetState()?.Players;
            if (players == null) return;

            var onlineIds = GetOnlineIds();
            if (players.All(p => onlineIds.Contains(p.NetId))) return;

            MapVote? fallbackVote = null;
            var allOnlineVoted = true;

            for (var i = 0; i < players.Count; i++)
            {
                if (!onlineIds.Contains(players[i].NetId)) continue;
                if (!votes[i].HasValue) 
                {
                    allOnlineVoted = false;
                }
                else 
                {
                    fallbackVote = votes[i];
                }
            }
            if (!allOnlineVoted || !fallbackVote.HasValue) return;
            
            var needsInvoke = false;
            for (var i = 0; i < votes.Count; i++)
            {
                if (votes[i].HasValue) continue;
                votes[i] = fallbackVote;
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
        public static void Postfix(TreasureRoomRelicSynchronizer __instance, Player player)
        {
            if (RunManager.Instance.NetService.Type != NetGameType.Host) return;

            var onlineIds = GetOnlineIds();
            if (!onlineIds.Contains(player.NetId)) return;

            var playerCollectionField = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_playerCollection");
            var votesField = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_votes");
            var currentRelicsField = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_currentRelics");

            var playerCollection = playerCollectionField?.GetValue(__instance) as IPlayerCollection;
            var votesList = votesField?.GetValue(__instance) as IList;
            var currentRelics = currentRelicsField?.GetValue(__instance) as IEnumerable; 

            if (playerCollection == null || votesList == null || currentRelics == null) return;

            if (playerCollection.Players.All(p => onlineIds.Contains(p.NetId))) return;

            var relicCount = currentRelics.Cast<object>().Count();
            if (relicCount == 0) return;

            var usedIndices = new HashSet<int>();
            
            for (var i = 0; i < playerCollection.Players.Count; i++)
            {
                if (i >= votesList.Count) break;
                
                var pId = playerCollection.Players[i].NetId;
                if (!onlineIds.Contains(pId)) continue;
                if (!HasVoted(votesList[i], out var vIndex))
                {
                    return;
                }
                if (vIndex.HasValue)
                {
                    usedIndices.Add(vIndex.Value);
                }
            }

            for (var i = 0; i < playerCollection.Players.Count; i++)
            {
                if (i >= votesList.Count) break;

                var ghostPlayer = playerCollection.Players[i];
                if (onlineIds.Contains(ghostPlayer.NetId) || HasVoted(votesList[i], out _)) continue;

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

                if (pickAction != null)
                {
                    EnqueueGhostAction(pickAction, ghostPlayer.NetId);
                }
                else
                {
                    Log.Error($"[DirectConnectIP] 无法实例化 PickRelicAction，代管拿遗物失败！");
                }
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

                // 旧版逻辑：已投票的值会装箱为 int
                if (type == typeof(int))
                {
                    votedIndex = (int)voteObj;
                    return true;
                }

                return false;
            }
        }
    }
    
    [HarmonyPatch(typeof(NHandImageCollection), "UpdateHandVisibility")]
    public static class TreasureHandExceptionHandlerPatch
    {
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
    // 战斗托管：兜底回合结束
    // ==========================================
    [HarmonyPatch(typeof(CombatManager), nameof(CombatManager.SetReadyToEndTurn))]
    public static class CombatTurnEndTakeoverPatch
    {
        public static void Postfix(CombatManager __instance, Player player)
        {
            if (RunManager.Instance.NetService.Type != NetGameType.Host) return;

            var onlineIds = GetOnlineIds();
            if (!onlineIds.Contains(player.NetId)) return;

            var state = __instance.DebugOnlyGetState();
            if (state == null || !__instance.IsInProgress || state.CurrentSide != CombatSide.Player) return;

            if (state.Players.All(p => onlineIds.Contains(p.NetId))) return;

            var allOnlineReady = state.Players.Where(p => onlineIds.Contains(p.NetId) && p.Creature.IsAlive).All(__instance.IsPlayerReadyToEndTurn);
            if (!allOnlineReady) return;

            var roundNumber = state.RoundNumber;
            foreach (var p in state.Players)
            {
                if (onlineIds.Contains(p.NetId) || !p.Creature.IsAlive || __instance.IsPlayerReadyToEndTurn(p)) continue;
                var endTurnAction = new EndPlayerTurnAction(p, roundNumber);
                EnqueueGhostAction(endTurnAction, p.NetId);
            }
        }
    }

    [HarmonyPatch(typeof(CombatManager), nameof(CombatManager.SetReadyToBeginEnemyTurn))]
    public static class SetReadyToBeginEnemyTakeoverPatch
    {
        public static void Postfix(CombatManager __instance, Player player)
        {
            if (RunManager.Instance.NetService.Type != NetGameType.Host) return;

            var onlineIds = GetOnlineIds();
            if (!onlineIds.Contains(player.NetId)) return;

            var readySet = (HashSet<Player>)AccessTools.Field(typeof(CombatManager), "_playersReadyToBeginEnemyTurn")?.GetValue(__instance);
            if (readySet == null) return;
                
            var state = __instance.DebugOnlyGetState();
            if (state == null) return;

            if (state.Players.All(p => onlineIds.Contains(p.NetId))) return;

            var allOnlineReady = state.Players.Where(p => onlineIds.Contains(p.NetId)).All(p => readySet.Contains(p));
            if (!allOnlineReady) return;
            
            foreach (var p in state.Players.Where(p => !onlineIds.Contains(p.NetId)))
            {
                if (readySet.Contains(p)) continue;
                var readyAction = new ReadyToBeginEnemyTurnAction(p);
                EnqueueGhostAction(readyAction, p.NetId);
            }
        }
    }

    // ==========================================
    // 关卡跳转托管
    // ==========================================
    [HarmonyPatch(typeof(ActChangeSynchronizer), nameof(ActChangeSynchronizer.OnPlayerReady))]
    public static class ActChangeGhostPatch
    {
        public static void Postfix(ActChangeSynchronizer __instance, Player player)
        {
            if (RunManager.Instance.NetService.Type != NetGameType.Host) return;

            var onlineIds = GetOnlineIds();
            if (!onlineIds.Contains(player.NetId)) return;

            var stateField = AccessTools.Field(typeof(ActChangeSynchronizer), "_runState");
            if (stateField?.GetValue(__instance) is not RunState state) return;

            if (state.Players.All(p => onlineIds.Contains(p.NetId))) return;

            var readyPlayersField = AccessTools.Field(typeof(ActChangeSynchronizer), "_readyPlayers");
            if (readyPlayersField?.GetValue(__instance) is not List<bool> readyPlayers) return;

            var allOnlineReady = !state.Players.Where((t, i) => onlineIds.Contains(t.NetId) && !readyPlayers[i]).Any();
            if (!allOnlineReady) return;

            for (var i = 0; i < state.Players.Count; i++)
            {
                var ghostPlayer = state.Players[i];
                if (onlineIds.Contains(ghostPlayer.NetId) || readyPlayers[i]) continue;
                var voteAction = new VoteToMoveToNextActAction(ghostPlayer);
                EnqueueGhostAction(voteAction, ghostPlayer.NetId);
            }
        }
    }

    // ==========================================
    // 战斗底层序列化状态同步托管
    // ==========================================
    [HarmonyPatch(typeof(CombatStateSynchronizer), nameof(CombatStateSynchronizer.StartSync))]
    public static class CombatSyncTakeoverPatch
    {
        public static void Postfix(CombatStateSynchronizer __instance)
        {
            var netService = (INetGameService)AccessTools.Field(typeof(CombatStateSynchronizer), "_netService")?.GetValue(__instance);
            if (netService is not { Type: NetGameType.Host }) return;

            var runState = (RunState)AccessTools.Field(typeof(CombatStateSynchronizer), "_runState")?.GetValue(__instance);
            var syncData = (Dictionary<ulong, SerializablePlayer>)AccessTools.Field(typeof(CombatStateSynchronizer), "_syncData")?.GetValue(__instance);
            if (runState == null || syncData == null) return;

            var onlineIds = GetOnlineIds();
            if (runState.Players.All(p => onlineIds.Contains(p.NetId))) return;

            foreach (var ghostPlayer in runState.Players)
            {
                if (onlineIds.Contains(ghostPlayer.NetId)) continue;
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
            catch (Exception ex)
            {
                Log.Error($"[DirectConnectIP] CombatSync 代管发生未知异常: {ex}");
            }
        }
    }
    
    [HarmonyPatch(typeof(CombatStateSynchronizer), "OnSyncPlayerMessageReceived")]
    public static class CombatSyncReceiveTakeoverPatch
    {
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
        public static void Postfix(EventSynchronizer __instance)
        {
            if (RunManager.Instance.NetService.Type != NetGameType.Host) return;

            var playerCollectionField = AccessTools.Field(typeof(EventSynchronizer), "_playerCollection");
            var isSharedProperty = AccessTools.Property(typeof(EventSynchronizer), "IsShared");
            var playerVotesField = AccessTools.Field(typeof(EventSynchronizer), "_playerVotes");

            var playerCollection = playerCollectionField?.GetValue(__instance) as IPlayerCollection;
            var isShared = isSharedProperty != null && (bool)isSharedProperty.GetValue(__instance)!;

            if (playerCollection == null) return;

            var onlineIds = GetOnlineIds();
            if (playerCollection.Players.All(p => onlineIds.Contains(p.NetId))) return;

            if (isShared)
            {
                if (playerVotesField?.GetValue(__instance) is not List<uint?> playerVotes) return;

                var allOnlineVoted = true;
                uint? fallbackVote = null;

                for (var i = 0; i < playerCollection.Players.Count; i++)
                {
                    if (!onlineIds.Contains(playerCollection.Players[i].NetId)) continue;
                    if (!playerVotes[i].HasValue)
                    {
                        allOnlineVoted = false;
                        break;
                    }
                    fallbackVote = playerVotes[i];
                }

                if (!allOnlineVoted || !fallbackVote.HasValue) return;
                for (var i = 0; i < playerCollection.Players.Count; i++)
                {
                    if (onlineIds.Contains(playerCollection.Players[i].NetId) || playerVotes[i].HasValue)
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

                var allOnlineFinished = !playerCollection.Players.Where((t, i) => onlineIds.Contains(t.NetId) && !events[i].IsFinished).Any();
                if (!allOnlineFinished) return;
                
                for (var i = 0; i < playerCollection.Players.Count; i++)
                {
                    var ghostPlayer = playerCollection.Players[i];
                    if (onlineIds.Contains(ghostPlayer.NetId) || events[i].IsFinished) continue;
                    
                    var chooseMethod = AccessTools.Method(typeof(EventSynchronizer), "ChooseOptionForEvent");
                    var safeGuard = 0; // 防死循环安全锁
                    
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
        public static void Prefix(PlayerChoiceSynchronizer __instance, Player player, uint choiceId)
        {
            if (RunManager.Instance.NetService.Type != NetGameType.Host) return;

            var onlineIds = GetOnlineIds();
            if (onlineIds.Contains(player.NetId)) return;

            Log.Info($"[DirectConnectIP] 侦测到等待离线玩家 {player.NetId} 的选择 (ChoiceId: {choiceId})，通过原生接口注入默认选项...");

            var defaultNetResult = PlayerChoiceResult.FromIndex(0).ToNetData();
            __instance.ReceiveReplayChoice(player, choiceId, defaultNetResult);
        }
    }
    
    // ==========================================
    // 双开存档冲突拦截
    // ==========================================
    [HarmonyPatch(typeof(GodotFileIo), nameof(GodotFileIo.RenameFile))]
    public static class LocalTestingRenameFilePatch
    {
        public static Exception Finalizer(Exception __exception)
        {
            if (__exception == null || !__exception.GetType().Name.Contains("SaveException")) return __exception;
            Log.Warn($"[DirectConnectIP] 拦截了双开重命名冲突: {__exception.Message}");
            return null;
        }
    }

    [HarmonyPatch(typeof(GodotFileIo), nameof(GodotFileIo.WriteFile), typeof(string), typeof(byte[]))]
    public static class LocalTestingWriteFileBytesPatch
    {
        public static Exception Finalizer(Exception __exception)
        {
            if (__exception == null || !__exception.GetType().Name.Contains("SaveException")) return __exception;
            Log.Warn($"[DirectConnectIP] 拦截了双开字节写入冲突: {__exception.Message}");
            return null;
        }
    }

    [HarmonyPatch(typeof(GodotFileIo), nameof(GodotFileIo.WriteFile), typeof(string), typeof(string))]
    public static class LocalTestingWriteFileStringPatch
    {
        public static Exception Finalizer(Exception __exception)
        {
            if (__exception == null || !__exception.GetType().Name.Contains("SaveException")) return __exception;
            Log.Warn($"[DirectConnectIP] 拦截了双开文本写入冲突: {__exception.Message}");
            return null;
        }
    }
}