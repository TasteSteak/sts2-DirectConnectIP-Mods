using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using System;
using System.Linq;
using System.Reflection;
using DirectConnectIP.Helpers;

namespace DirectConnectIP;

[ModInitializer("Initialize")]
public static class ModEntry
{
    public static readonly ModConfigManager Config = new();
    private static readonly string Version =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ??
        "0.0.0";

    public static void Initialize()
    {
        Log.Info($"[DirectConnectIP] 当前版本：v{Version}");

        var harmony = new Harmony("steak.sts2.directconnectip");
        var assembly = Assembly.GetExecutingAssembly();

        var patchTypes = AccessTools.GetTypesFromAssembly(assembly).Where(t => t.HasHarmonyAttribute());

        foreach (var type in patchTypes)
        {
            try
            {
                harmony.CreateClassProcessor(type).Patch();
            }
            catch (Exception ex)
            {
                Log.Error($"补丁类 {type.FullName} 应用失败: {ex}");
            }
        }
    }
}
