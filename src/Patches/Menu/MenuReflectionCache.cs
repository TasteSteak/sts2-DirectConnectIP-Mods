using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Multiplayer;

namespace DirectConnectIP.Patches.Menu;

public static class MenuReflectionCache
{
    public static readonly FieldInfo SubmenuStackField = AccessTools.Field(typeof(NSubmenu), "_stack");
    public static readonly FieldInfo LoadingOverlayField = AccessTools.Field(typeof(NMultiplayerSubmenu), "_loadingOverlay");
    public static readonly FieldInfo NetHostField = AccessTools.Field(typeof(NetHostGameService), "_netHost");

    public static readonly MethodInfo OnHostPressedMethod = AccessTools.Method(typeof(NMultiplayerSubmenu), "OnHostPressed");
    public static readonly MethodInfo StartLoadMethod = AccessTools.Method(typeof(NMultiplayerSubmenu), "StartLoad");
}