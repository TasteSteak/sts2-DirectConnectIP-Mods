using Godot;
using MegaCrit.Sts2.Core.Commands;

namespace DirectConnectIP.Helpers;

public sealed partial class SfxButton : Button
{
    private static string ClickedSfx => "event:/sfx/ui/clicks/ui_click";
    private static string HoveredSfx => "event:/sfx/ui/clicks/ui_hover";

    public override void _Ready()
    {
        base._Ready();
            
        Pressed += OnPress;
        MouseEntered += OnHover;
        FocusEntered += OnHover;
    }

    private static void OnPress()
    {
        if (!string.IsNullOrEmpty(ClickedSfx))
        {
            SfxCmd.Play(ClickedSfx);
        }
    }

    private void OnHover()
    {
        if (!string.IsNullOrEmpty(HoveredSfx) && !Disabled)
        {
            SfxCmd.Play(HoveredSfx);
        }
    }
}