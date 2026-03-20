using System;
using System.Collections.Generic;
using System.Linq;
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

    [HarmonyPatch(typeof(MapSelectionSynchronizer), nameof(MapSelectionSynchronizer.PlayerVotedForMapCoord))]
    public static class MapSelectionGhostPatch
    {
        public static void Postfix(MapSelectionSynchronizer __instance)
        {
            if (RunManager.Instance.NetService.Type != NetGameType.Host) return;

            var votes = (List<MapVote?>)AccessTools.Field(typeof(MapSelectionSynchronizer), "_votes")?.GetValue(__instance);
            if (votes == null) return;

            var onlineIds = GetOnlineIds();
            MapVote? fallbackVote = null;
            var allOnlineVoted = true;

            var players = RunManager.Instance.DebugOnlyGetState()?.Players;
            if (players == null) return;

            for (var i = 0; i < players.Count; i++)
            {
                if (!onlineIds.Contains(players[i].NetId)) continue;
                if (!votes[i].HasValue) allOnlineVoted = false;
                else fallbackVote = votes[i];
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

    [HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.OnPicked))]
    public static class TreasureUnblockPatch
    {
        public static void Postfix(TreasureRoomRelicSynchronizer __instance, Player player)
        {
            if (RunManager.Instance.NetService.Type != NetGameType.Host) return;

            var onlineIds = GetOnlineIds();
            if (!onlineIds.Contains(player.NetId)) return;

            var votes = (List<int?>)AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_votes")?.GetValue(__instance);
            var playerCollection = (IPlayerCollection)AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_playerCollection")?.GetValue(__instance);
            if (votes == null || playerCollection == null) return;

            var allOnlineVoted = !playerCollection.Players.Where((t, i) => onlineIds.Contains(t.NetId) && !votes[i].HasValue).Any();
            if (!allOnlineVoted) return;

            var relicCount = playerCollection.Players.Count;
            var currentRelicsProp = AccessTools.Property(typeof(TreasureRoomRelicSynchronizer), "CurrentRelics");
            if (currentRelicsProp != null && currentRelicsProp.GetValue(__instance) is System.Collections.IEnumerable relics)
            {
                var count = relics.Cast<object>().Count();
                relicCount = count;
            }

            var pickedIndices = new HashSet<int>();
            for (var i = 0; i < playerCollection.Players.Count; i++)
            {
                if (onlineIds.Contains(playerCollection.Players[i].NetId) && votes[i].HasValue)
                {
                    pickedIndices.Add(votes[i].Value);
                }
            }

            var nextAvailableIndex = 0;
            for (var i = 0; i < playerCollection.Players.Count; i++)
            {
                var ghostPlayer = playerCollection.Players[i];
                if (onlineIds.Contains(ghostPlayer.NetId) || votes[i].HasValue) continue;

                while (pickedIndices.Contains(nextAvailableIndex) && nextAvailableIndex < relicCount)
                {
                    nextAvailableIndex++;
                }

                var pickIndex = nextAvailableIndex < relicCount ? nextAvailableIndex : 0;
                
                pickedIndices.Add(pickIndex); 

                var pickAction = new PickRelicAction(ghostPlayer, pickIndex);
                EnqueueGhostAction(pickAction, ghostPlayer.NetId);
            }
        }
    }

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
            var roundNumber = state.RoundNumber;

            foreach (var p in state.Players)
            {
                if (onlineIds.Contains(p.NetId) || !p.Creature.IsAlive || CombatManager.Instance.IsPlayerReadyToEndTurn(p)) continue;
                var endTurnAction = new EndPlayerTurnAction(p, roundNumber);
                EnqueueGhostAction(endTurnAction, p.NetId);
            }
        }
    }

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

    [HarmonyPatch(typeof(NHandImageCollection), "UpdateHandVisibility")]
    public static class TreasureHandExceptionHandlerPatch
    {
        public static Exception Finalizer(Exception __exception) => null;
    }

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

            foreach (var ghostPlayer in runState.Players)
            {
                if (onlineIds.Contains(ghostPlayer.NetId)) continue;
                var serializedGhost = ghostPlayer.ToSerializable();
                
                syncData[ghostPlayer.NetId] = serializedGhost;
                var message = new SyncPlayerDataMessage { player = serializedGhost };
                netService.SendMessage(message);
            }
            AccessTools.Method(typeof(CombatStateSynchronizer), "CheckSyncCompleted")?.Invoke(__instance, null);
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
            AccessTools.Method(typeof(CombatStateSynchronizer), "CheckSyncCompleted")?.Invoke(__instance, null);
            return false;
        }
    }

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
                    if (onlineIds.Contains(playerCollection.Players[i].NetId) || events[i].IsFinished) continue;
                    if (events[i].CurrentOptions.Count <= 0) continue;
                    var chooseMethod = AccessTools.Method(typeof(EventSynchronizer), "ChooseOptionForEvent");
                    chooseMethod?.Invoke(__instance, [playerCollection.Players[i], 0]);
                }
            }
        }
    }
    
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