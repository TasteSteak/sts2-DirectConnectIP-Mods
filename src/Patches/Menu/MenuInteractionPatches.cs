using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using DirectConnectIP.Helpers;
using DirectConnectIP.Commands;
using DirectConnectIP.Screens;

namespace DirectConnectIP.Patches.Menu;

[HarmonyPatch(typeof(NMultiplayerSubmenu), "UpdateButtons")]
public static class SubmenuUiPatch
{
    static void Postfix(NMultiplayerSubmenu __instance)
    {
        try
        {
            if (__instance.HasMeta("DirectConnect_Injected")) return;
            __instance.SetMeta("DirectConnect_Injected", true);

            var buttonContainer = __instance.GetNodeOrNull<Control>("ButtonContainer");
            if (buttonContainer == null) 
            {
                GD.PrintErr("[DirectConnectIP] UI 注入失败: 找不到 ButtonContainer 节点");
                return;
            }

            var joinBtn = ModUiHelper.CreateCustomSubmenuButton(
                newName: "JoinServer",
                locKeyPrefix: "MOD_DC_BTN_JOIN",
                bgColor: new Color(0.4f, 0.6f, 0.9f),
                onClickAction: _ => new JoinServerScreen(text => OnIpSubmitted(__instance, text)).OpenScreen(__instance),
                customIconFileName: "assets/submenu/join_server.png"
            );
            buttonContainer.AddChild(joinBtn);

            HijackButtonSignal(__instance, "ButtonContainer/HostButton", btn => {
                new HostModeSelectScreen(() => {
                    MenuReflectionCache.OnHostPressedMethod?.Invoke(__instance, [btn]);
                }).OpenScreen(__instance);
            });

            HijackButtonSignal(__instance, "ButtonContainer/LoadButton", btn => {
                new HostModeSelectScreen(() => {
                    MenuReflectionCache.StartLoadMethod?.Invoke(__instance, [btn]);
                }).OpenScreen(__instance);
            });
        }
        catch (Exception ex) 
        { 
            GD.PrintErr($"[DirectConnectIP] UI 注入或劫持失败: {ex}"); 
        }
    }

    private static void HijackButtonSignal(NMultiplayerSubmenu menu, string nodePath, Action<NButton> onIntercepted)
    {
        var btn = menu.GetNodeOrNull<NSubmenuButton>(nodePath);
        if (btn == null) return;

        var connections = btn.GetSignalConnectionList(NClickableControl.SignalName.Released);
        foreach (var conn in connections)
        {
            var callable = conn["callable"].As<Callable>();
            if (callable.Target != menu) continue;
            
            if (btn.IsConnected(NClickableControl.SignalName.Released, callable))
            {
                btn.Disconnect(NClickableControl.SignalName.Released, callable);
            }
        }

        var myCallable = Callable.From<Variant>(_ => onIntercepted(btn)); 
        if (!btn.IsConnected(NClickableControl.SignalName.Released, myCallable))
        {
            btn.Connect(NClickableControl.SignalName.Released, myCallable);
        }
    }

    private static bool OnIpSubmitted(Node parentMenu, string inputText)
    {
        if (!IpAddressParser.TryParse(inputText, out var host, out var port)) return false;

        new LoadingScreen(inputText).OpenScreen(parentMenu);
        HostModeSettings.CurrentMode = HostMode.ENet;
            
        try { new ConnectionCmd().Process(null, [host, port.ToString()]); }
        catch (Exception ex) { GD.PrintErr($"[DirectConnectIP] 连接异常: {ex}"); }
        return true;
    }
}