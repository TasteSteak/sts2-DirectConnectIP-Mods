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
    
    public static void Initialize()
    {
        Log.Info("[DirectConnectIP] 当前版本：v1.1.4");
        Log.Info("[DirectConnectIP] 加载成功！感谢你选择了我们的模组，我们致力于为游戏社区提供更好的体验！");

        var harmony = new Harmony("steak.sts2.directconnectip");
        var assembly = Assembly.GetExecutingAssembly();

        var patchTypes = AccessTools.GetTypesFromAssembly(assembly)
            .Where(t => t.HasHarmonyAttribute());

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