using System.Threading;
using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace DirectConnectIP.Helpers;

public static class MenuStateManager
{
    public static CanvasLayer ActiveInputLayer { get; set; }
    public static CanvasLayer ActiveLoadingLayer { get; set; }
    private static bool _isHookedToModal;

    public static CancellationTokenSource ConnectionCts { get; set; }
    public static bool IsConnectionCancelled { get; set; }

    public static void CloseAllPanels()
    {
        if (GodotObject.IsInstanceValid(ActiveInputLayer)) ActiveInputLayer.QueueFree();
        if (GodotObject.IsInstanceValid(ActiveLoadingLayer)) ActiveLoadingLayer.QueueFree();
            
        ActiveInputLayer = null;
        ActiveLoadingLayer = null;
    }

    public static void HookModalContainer()
    {
        if (_isHookedToModal) return;

        if (NModalContainer.Instance == null) return;
        NModalContainer.Instance.Connect(Node.SignalName.ChildEnteredTree, Callable.From<Node>(OnOfficialModalAppeared));
        _isHookedToModal = true;
    }

    private static void OnOfficialModalAppeared(Node popupNode)
    {
        if (IsConnectionCancelled && popupNode.GetType().Name.Contains("ErrorPopup"))
        {
            Callable.From(() => {
                if (GodotObject.IsInstanceValid(popupNode)) popupNode.QueueFree();
                if (NModalContainer.Instance != null) NModalContainer.Instance.Clear();
            }).CallDeferred();
            return;
        }

        CloseAllPanels();
    }
}