using System;
using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;

namespace DirectConnectIP.Patches.Network;

public static class OfflineTakeoverUtility
{
    public static bool IsTakeoverEnabled() => ModEntry.Config.EnableOfflineTakeover;

    public static bool IsGhost(ulong netId)
    {
        if (LocalContext.NetId.HasValue && netId == LocalContext.NetId.Value)
        {
            return false;
        }

        var netService = RunManager.Instance.NetService;
        if (netService.Type != NetGameType.Host && netService.Type != NetGameType.Client)
        {
            return false;
        }

        return !GetOnlineIds().Contains(netId);
    }

    private static HashSet<ulong> GetOnlineIds()
    {
        var ids = new HashSet<ulong> { LocalContext.NetId.HasValue ? LocalContext.NetId.Value : ModEntry.Config.LocalPlayerId };

        var netService = RunManager.Instance.NetService;
        if (!netService.IsConnected) return ids;

        switch (netService.Type)
        {
            case NetGameType.Host when netService is NetHostGameService hostService:
            {
                foreach (var peer in hostService.ConnectedPeers)
                {
                    ids.Add(peer.peerId);
                }

                break;
            }
            case NetGameType.Client:
            {
                foreach (var id in PlayerNameRegistry.RemoteNames.Keys)
                {
                    ids.Add(id);
                }

                break;
            }
            case NetGameType.None:
            case NetGameType.Singleplayer:
            case NetGameType.Replay:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        return ids;
    }

    public static void EnqueueGhostAction(GameAction action, ulong ghostNetId)
    {
        try
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
        catch (Exception ex)
        {
            Log.Error($"[DirectConnectIP] 动作 {action} 发包失败: {ex}");
        }
    }
}