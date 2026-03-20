#nullable enable
using System.Threading;
using Godot;
using DirectConnectIP.Localization;
using DirectConnectIP.Helpers;
using MegaCrit.Sts2.Core.Commands;

namespace DirectConnectIP.Screens;

public partial class LoadingScreen(string ipText) : DirectUiScreen
{
    private Label _animationLabel = null!;
    private SfxButton _cancelBtn = null!;
    private Godot.Timer? _timer;
    
    private static readonly string[] LoadingFrames = ["Ooo", "oOo", "ooO", "oOo"];
    private static string HoveredSfx => "event:/sfx/ui/clicks/ui_hover";
    private int _currentFrame;

    protected override Vector2 PanelSize => new(500, 300);
    protected override int VBoxSeparation => 30;
    protected override Control InitialFocusedControl => _cancelBtn;

    protected override void BuildUi()
    {
        var connectingText = string.Format(LocTexts.LoadingConnecting, ipText);
        var titleLabel = CreateTitleLabel(connectingText, 32);
        Vbox.AddChild(titleLabel);

        _animationLabel = CreateTitleLabel(LoadingFrames[0], 24);
        _animationLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
        Vbox.AddChild(_animationLabel);

        _cancelBtn = CreateButton(LocTexts.BtnCancelConnection, new Color(0.6f, 0.2f, 0.2f), new Vector2(200, 50), 22);
        _cancelBtn.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        
        _cancelBtn.Pressed += CloseScreen;
        
        Vbox.AddChild(_cancelBtn);

        _timer = new Godot.Timer { 
            WaitTime = 0.3f, 
            Autostart = true, 
            OneShot = false 
        };
        _timer.Timeout += UpdateAnimation;
        AddChild(_timer);
    }

    public override void OpenScreen(Node parent, bool closeOthers = true)
    {
        MenuStateManager.ConnectionCts = new CancellationTokenSource();
        MenuStateManager.IsConnectionCancelled = false;

        base.OpenScreen(parent, closeOthers);
        MenuStateManager.ActiveLoadingLayer = this;
    }

    private void UpdateAnimation()
    {
        if (!IsInstanceValid(_animationLabel)) return;
        _currentFrame = (_currentFrame + 1) % LoadingFrames.Length;
        _animationLabel.Text = LoadingFrames[_currentFrame];
        SfxCmd.Play(HoveredSfx);
    }

    protected override void OnSubmenuClosed()
    {
        if (MenuStateManager.ConnectionCts != null && !MenuStateManager.ConnectionCts.IsCancellationRequested)
        {
            MenuStateManager.IsConnectionCancelled = true;
            MenuStateManager.ConnectionCts.Cancel();
        }

        if (IsInstanceValid(_timer))
        {
            _timer!.Stop();
        }
        
        if (MenuStateManager.ActiveLoadingLayer == this)
        {
            MenuStateManager.ActiveLoadingLayer = null;
        }
    }
}