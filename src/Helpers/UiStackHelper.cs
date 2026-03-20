using System;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace DirectConnectIP.Helpers;

public static class UiStackHelper
{
    public static void PushScreen<T>(Action<T> initializeAction) where T : NSubmenu
    {
        var stack = NGame.Instance!.MainMenu!.SubmenuStack;

        var screen = stack.GetSubmenuType<T>();
        initializeAction?.Invoke(screen);
        stack.Push(screen);
    }
}