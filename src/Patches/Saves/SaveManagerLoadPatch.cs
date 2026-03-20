using HarmonyLib;
using MegaCrit.Sts2.Core.Saves;

namespace DirectConnectIP.Patches.Saves;

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.LoadAndCanonicalizeMultiplayerRunSave))]
public static class SaveManagerLoadPatch
{
    static bool Prefix(ref ulong localPlayerId)
    {
        if (HostModeSettings.CurrentMode != HostMode.ENet) return true;
        localPlayerId = ModEntry.Config.LocalPlayerId;
        return true;
    }
}