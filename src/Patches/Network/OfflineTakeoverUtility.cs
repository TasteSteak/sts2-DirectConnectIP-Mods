using System;
using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace DirectConnectIP.Patches.Network;

/// <summary>
/// 离线接管的公共核心工具类
/// </summary>
public static class OfflineTakeoverUtility
{
    public static bool IsTakeoverEnabled() => ModEntry.Config.EnableOfflineTakeover;
    public static bool IsGhost(ulong netId) => !GetOnlineIds().Contains(netId);

    /// <summary>
    /// 智能双端获取在线玩家名单
    /// </summary>
    private static HashSet<ulong> GetOnlineIds()
    {
        var ids = new HashSet<ulong> { ModEntry.Config.LocalPlayerId };
        var netService = RunManager.Instance.NetService;

        if (!netService.IsConnected) return ids;

        if (netService.Type == NetGameType.Host && netService is NetHostGameService hostService)
        {
            foreach (var peer in hostService.ConnectedPeers)
            {
                ids.Add(peer.peerId);
            }
        }
        else if (netService.Type == NetGameType.Client)
        {
            foreach (var id in PlayerNameRegistry.RemoteNames.Keys)
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    /// <summary>
    /// 反射强行调用底层队列接口。
    /// 绕过主机的自动盖章，强行以 ghostNetId 的身份将动作广播给所有客机！
    /// </summary>
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