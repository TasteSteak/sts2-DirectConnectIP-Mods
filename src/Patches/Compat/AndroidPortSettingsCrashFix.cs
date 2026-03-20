using System.Reflection;
using Godot;
using HarmonyLib;

namespace DirectConnectIP.Patches.Compat;

/// <summary>
/// 兼容性模块：用于动态检测并修复特定移植版/魔改版的环境 Bug。
/// </summary>
[HarmonyPatch]
public static class AndroidPortSettingsCrashFix
{
    static bool Prepare()
    {
        if (!ModEntry.Config.EnableAndroidCompatFix)
        {
            return false; // 配置未开启，直接跳过
        }

        var type = AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Screens.Settings.NSettingsScreen");
        if (type == null) return false;

        var methodExists = AccessTools.Method(type, "ConfigureAndroidGraphicsEntries") != null;
        if (methodExists)
        {
            MegaCrit.Sts2.Core.Logging.Log.Info("[DirectConnectIP] 安卓兼容修复模块已启用。");
        }
        return methodExists;
    }

    static MethodBase TargetMethod()
    {
        var type = AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Screens.Settings.NSettingsScreen");
        return AccessTools.Method(type, "ConfigureAndroidGraphicsEntries");
    }

    static bool Prefix()
    {
        GD.Print("[DirectConnectIP-Compat] 检测到移动移植版环境，已自动抑制 Settings 崩溃 Bug。");
        return false; 
    }
}