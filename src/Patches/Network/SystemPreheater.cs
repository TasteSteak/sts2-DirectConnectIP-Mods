#nullable enable
using System;
using System.Reflection;
using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.PeerInput;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Logging;

namespace DirectConnectIP.Patches.Network;

public static class SystemPreheater
{
    public static PeerInputSynchronizer? InputSyncInstance { get; private set; }
    private static readonly MethodInfo? GetOrCreateMethod = AccessTools.Method(typeof(PeerInputSynchronizer), "GetOrCreateStateForPlayer");

    public static void SetInstance(PeerInputSynchronizer instance) => InputSyncInstance = instance;
    public static void ClearInstance() => InputSyncInstance = null;

    public static void PrewarmPlayer(ulong playerId)
    {
        if (InputSyncInstance == null || GetOrCreateMethod == null) return;
        try
        {
            GetOrCreateMethod.Invoke(InputSyncInstance, [playerId]);
        }
        catch 
        {
            // 静默
        }
    }

    public static void PrewarmAllKnownPlayers()
    {
        foreach (var playerId in PlayerNameRegistry.RemoteNames.Keys)
        {
            PrewarmPlayer(playerId);
        }
    }

    public static void PatchLegacyPeerInputState(Harmony harmony)
    {
        var forceGetMethod = AccessTools.Method(typeof(PeerInputSynchronizer), "ForceGetStateForPlayer");
        var prefix = AccessTools.Method(typeof(PeerInputStateGhostPatch), nameof(PeerInputStateGhostPatch.Prefix));
        if (forceGetMethod == null || prefix == null) return;

        try
        {
            harmony.Patch(forceGetMethod, prefix: new HarmonyMethod(prefix));
        }
        catch (Exception ex)
        {
            Log.Error($"[DirectConnectIP] 旧版输入状态兜底补丁应用失败: {ex}");
        }
    }
}

[HarmonyPatch(typeof(PeerInputSynchronizer), MethodType.Constructor, typeof(INetGameService))]
public static class PeerInputSynchronizerInitPatch
{
    public static void Postfix(PeerInputSynchronizer __instance)
    {
        SystemPreheater.SetInstance(__instance);
        SystemPreheater.PrewarmAllKnownPlayers();
    }
}

[HarmonyPatch(typeof(PeerInputSynchronizer), nameof(PeerInputSynchronizer.Dispose))]
public static class PeerInputSynchronizerDisposePatch
{
    public static void Postfix(PeerInputSynchronizer __instance)
    {
        if (SystemPreheater.InputSyncInstance == __instance)
        {
            SystemPreheater.ClearInstance();
        }
    }
}

// 旧版游戏中 ForceGetStateForPlayer 在状态缺失时会抛异常。
// 新版 0105 已移除该方法，因此由 SystemPreheater 手动按方法存在性打补丁，
// 避免 Harmony 自动扫描在新版游戏里报告 Undefined target method。
public static class PeerInputStateGhostPatch
{
    private static readonly MethodInfo? GetOrCreateMethod = AccessTools.Method(typeof(PeerInputSynchronizer), "GetOrCreateStateForPlayer");

    public static bool Prefix(PeerInputSynchronizer __instance, ulong playerId, ref object __result)
    {
        try
        {
            if (GetOrCreateMethod != null)
            {
                __result = GetOrCreateMethod.Invoke(__instance, [playerId])!;
                return false; 
            }
        }
        catch 
        {
            // 静默
        }
        return true;
    }
}

// ==========================================
// 拦截官方 UI 渲染层过早收到鼠标数据时的空指针崩溃
// ==========================================
[HarmonyPatch(typeof(NRemoteMouseCursorContainer), "OnInputStateChanged")]
public static class RemoteMouseCursorFailsafePatch
{
    private static readonly MethodInfo? GetCursorMethod = AccessTools.Method(typeof(NRemoteMouseCursorContainer), "GetCursor");
    private static readonly MethodInfo? AddCursorMethod = AccessTools.Method(typeof(NRemoteMouseCursorContainer), "AddCursor");

    public static bool Prefix(NRemoteMouseCursorContainer __instance, ulong playerId)
    {
        try
        {
            if (!GodotObject.IsInstanceValid(__instance) || !__instance.IsInsideTree()) return false;
            if (GetCursorMethod == null || AddCursorMethod == null) return true;
            
            var cursor = GetCursorMethod.Invoke(__instance, [playerId]);
            if (cursor != null) return true;
            
            AddCursorMethod.Invoke(__instance, [playerId]);
            cursor = GetCursorMethod.Invoke(__instance, [playerId]);
            
            return cursor != null;
        }
        catch
        {
            return false;
        }
    }
}
