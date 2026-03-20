using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Platform.Null;

namespace DirectConnectIP.Patches.Network;

public static class PlayerNameRegistry
{
    public static readonly Dictionary<ulong, string> RemoteNames = new();
}

[HarmonyPatch(typeof(NullPlatformUtilStrategy), "GetLocalPlayerId")]
public static class GetLocalPlayerIdPatch
{
    public static bool Prefix(ref ulong __result)
    {
        __result = ModEntry.Config.LocalPlayerId;
        return false; 
    }
}

[HarmonyPatch(typeof(NullPlatformUtilStrategy), "GetPlayerName")]
public static class GetPlayerNamePatch
{
    public static bool Prefix(ulong playerId, ref string __result)
    {
        if (playerId == ModEntry.Config.LocalPlayerId)
        {
            __result = ModEntry.Config.LocalPlayerName;
            return false;
        }

        if (PlayerNameRegistry.RemoteNames.TryGetValue(playerId, out string remoteName))
        {
            __result = remoteName;
            return false;
        }

        __result = $"玩家 {playerId}";
        return false;
    }
}