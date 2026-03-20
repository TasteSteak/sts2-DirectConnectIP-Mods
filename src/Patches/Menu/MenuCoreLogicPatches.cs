using System;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.DailyRun;
using MegaCrit.Sts2.Core.Platform.Steam;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using DirectConnectIP.Network;
using MegaCrit.Sts2.Core.Entities.Multiplayer;

namespace DirectConnectIP.Patches.Menu;

[HarmonyPatch(typeof(NMultiplayerHostSubmenu), nameof(NMultiplayerHostSubmenu.StartHostAsync))]
public static class NMultiplayerHostSubmenuPatch
{
    static bool Prefix(GameMode gameMode, Control loadingOverlay, NSubmenuStack stack, ref Task __result)
    {
        __result = RunCustomNewHostAsync(gameMode, loadingOverlay, stack);
        return false;
    }

    private static async Task RunCustomNewHostAsync(GameMode gameMode, Control loadingOverlay, NSubmenuStack stack)
    {
        if (loadingOverlay != null) loadingOverlay.Visible = true;
        try
        {
            var netService = new NetHostGameService();
            var error = (HostModeSettings.CurrentMode == HostMode.Steam && SteamInitializer.Initialized)
                ? await netService.StartSteamHost(4)
                : ServerLauncher.StartDirectHost(netService, 33771);

            if (!error.HasValue)
                ServerLauncher.NavigateToHostScreen(gameMode, stack, netService);
            else
                ServerLauncher.ShowError(error.Value);
        }
        finally { if (loadingOverlay != null) loadingOverlay.Visible = false; }
    }
}

[HarmonyPatch(typeof(NMultiplayerSubmenu), "StartHostAsync")]
public static class LoadGameStartHostAsyncPatch
{
    static bool Prefix(NMultiplayerSubmenu __instance, SerializableRun run, ref Task __result)
    {
        __result = RunCustomLoadHostAsync(__instance, run);
        return false;
    }

    private static async Task RunCustomLoadHostAsync(NMultiplayerSubmenu instance, SerializableRun run)
    {
        var overlay = MenuReflectionCache.LoadingOverlayField?.GetValue(instance) as Control;
        var stack = MenuReflectionCache.SubmenuStackField?.GetValue(instance) as NSubmenuStack;

        if (overlay != null) overlay.Visible = true;
        try
        {
            var netService = new NetHostGameService();
            var error = HostModeSettings.CurrentMode == HostMode.Steam && SteamInitializer.Initialized
                ? await netService.StartSteamHost(4)
                : ServerLauncher.StartDirectHost(netService, 33771);

            if (!error.HasValue && stack != null)
                ServerLauncher.NavigateToLoadScreen(stack, netService, run);
            else if (error.HasValue)
                ServerLauncher.ShowError(error.Value);
        }
        finally { if (overlay != null) overlay.Visible = false; }
    }
}

internal static class ServerLauncher
{
    public static NetErrorInfo? StartDirectHost(NetHostGameService netService, ushort port)
    {
        var directHost = new DirectHost(netService);
        var error = directHost.StartHost(port, HostModeSettings.MaxDirectPlayers, ModEntry.Config.LocalPlayerId);
        if (error.HasValue) return error;

        if (MenuReflectionCache.NetHostField == null) throw new Exception("字段 _netHost 丢失");
        MenuReflectionCache.NetHostField.SetValue(netService, directHost);
        return null;
    }

    public static void NavigateToHostScreen(GameMode mode, NSubmenuStack stack, NetHostGameService netService)
    {
        var max = HostModeSettings.MaxDirectPlayers;
        switch (mode)
        {
            case GameMode.Standard:
                var cs = stack.GetSubmenuType<NCharacterSelectScreen>();
                cs.InitializeMultiplayerAsHost(netService, max);
                stack.Push(cs);
                break;
            case GameMode.Daily:
                var ds = stack.GetSubmenuType<NDailyRunScreen>();
                ds.InitializeMultiplayerAsHost(netService);
                stack.Push(ds);
                break;
            default:
                var cus = stack.GetSubmenuType<NCustomRunScreen>();
                cus.InitializeMultiplayerAsHost(netService, max);
                stack.Push(cus);
                break;
        }
    }

    public static void NavigateToLoadScreen(NSubmenuStack stack, NetHostGameService netService, SerializableRun run)
    {
        if (run.Modifiers.Count > 0)
        {
            if (run.DailyTime.HasValue) {
                var sub = stack.GetSubmenuType<NDailyRunLoadScreen>();
                sub.InitializeAsHost(netService, run);
                stack.Push(sub);
            } else {
                var sub = stack.GetSubmenuType<NCustomRunLoadScreen>();
                sub.InitializeAsHost(netService, run);
                stack.Push(sub);
            }
        }
        else {
            var sub = stack.GetSubmenuType<NMultiplayerLoadGameScreen>();
            sub.InitializeAsHost(netService, run);
            stack.Push(sub);
        }
    }

    public static void ShowError(NetErrorInfo error)
    {
        var modal = NErrorPopup.Create(error);
        if (modal != null) NModalContainer.Instance!.Add(modal);
    }
}