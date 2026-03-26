using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;

namespace DirectConnectIP.Patches.Network;

public static class OfflineTakeoverCore
{
    private static readonly MethodInfo EnqueueActionMethod = AccessTools.Method(typeof(ActionQueueSynchronizer), "EnqueueAction");
    public static bool IsTakeoverEnabled() => ModEntry.Config is { EnableOfflineTakeover: true };

    public static bool IsGhost(ulong netId)
    {
        try
        {
            if (LocalContext.NetId == netId) return false;
            if (RunManager.Instance is not { NetService: { } netService }) return false;
            if (netService.Type is not (NetGameType.Host or NetGameType.Client)) return false;

            return !GetOnlineIds().Contains(netId);
        }
        catch
        {
            return false;
        }
    }

    private static HashSet<ulong> GetOnlineIds()
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
                ids.Add(peer.peerId);
            }
        }
        else if (netService.Type == NetGameType.Client && PlayerNameRegistry.RemoteNames != null)
        {
            foreach (var id in PlayerNameRegistry.RemoteNames.Keys)
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    public static void EnqueueGhostAction(GameAction action, ulong ghostNetId)
    {
        try
        {
            if (RunManager.Instance is not { ActionQueueSynchronizer: { } sync }) return;

            if (EnqueueActionMethod != null)
            {
                EnqueueActionMethod.Invoke(sync, [action, ghostNetId]);
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