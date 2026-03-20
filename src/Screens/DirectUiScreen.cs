#nullable enable
using System;
using DirectConnectIP.Helpers;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;

namespace DirectConnectIP.Screens;

public abstract partial class DirectUiScreen : CanvasLayer
{
    private Control? _lastFocusedControl;
    private DirectUiScreen? _previousScreen;
    private PanelContainer? _popupPanel;

    protected VBoxContainer Vbox { get; private set; } = null!;

    protected abstract Control? InitialFocusedControl { get; }
    protected abstract Vector2 PanelSize { get; }
    protected virtual int VBoxSeparation => 15;
    protected virtual bool ShouldInjectBackground => true;
    
    private static int ContentPadding => 25;

    public virtual void OpenScreen(Node parent, bool closeOthers = true)
    {
        if (closeOthers)
        {
            MenuStateManager.CloseAllPanels();
        }
        else if (MenuStateManager.ActiveInputLayer is DirectUiScreen currentActive)
        {
            _previousScreen = currentActive;
            _previousScreen.SetContentVisible(false);
        }
        
        MenuStateManager.HookModalContainer();
        MenuStateManager.ActiveInputLayer = this;
        Layer = 100;

        var fullScreenContainer = new Control();
        fullScreenContainer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        fullScreenContainer.MouseFilter = Control.MouseFilterEnum.Stop;
        AddChild(fullScreenContainer);

        if (ShouldInjectBackground)
        {
            InjectTimelineBackground(fullScreenContainer);
        }

        var centerContainer = new CenterContainer();
        centerContainer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        centerContainer.MouseFilter = Control.MouseFilterEnum.Ignore;
        fullScreenContainer.AddChild(centerContainer);

        _popupPanel = CreateStyledPanelContainer(PanelSize, new Color(0.12f, 0.12f, 0.15f, 0.95f), ContentPadding);
        centerContainer.AddChild(_popupPanel);

        Vbox = CreateVBoxContainer(VBoxSeparation);
        Vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        Vbox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _popupPanel.AddChild(Vbox);

        BuildUi();
        parent.AddChild(this);
            
        CallDeferred(MethodName.GrabInitialFocus);
        OnSubmenuOpened();
    }

    private void SetContentVisible(bool visible)
    {
        if (_popupPanel != null) _popupPanel.Visible = visible;
    }

    protected void CloseScreen()
    {
        if (_previousScreen != null && IsInstanceValid(_previousScreen))
        {
            _previousScreen.SetContentVisible(true);
            MenuStateManager.ActiveInputLayer = _previousScreen;
        }

        if (IsInstanceValid(_lastFocusedControl))
        {
            _lastFocusedControl!.GrabFocus();
        }

        OnSubmenuClosed();
        QueueFree();
    }

    public override void _Input(InputEvent @event)
    {
        base._Input(@event);
        if (MenuStateManager.ActiveInputLayer != this) return;
        var isCancelTriggered = false;
        try 
        {
            if (@event.IsActionPressed("ui_cancel", false, true))
            {
                isCancelTriggered = true;
            }
        } 
        catch { }

        if (!isCancelTriggered && @event is InputEventKey { Pressed: true } and { Echo: false, Keycode: Key.Escape or Key.Back })
        {
            isCancelTriggered = true;
        }

        if (!isCancelTriggered && @event is InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.B })
        {
            isCancelTriggered = true;
        }

        if (!isCancelTriggered) return;
        GetViewport().SetInputAsHandled();
        CallDeferred(MethodName.CloseScreen);
    }

    private void GrabInitialFocus()
    {
        if (IsInstanceValid(InitialFocusedControl))
        {
            InitialFocusedControl!.GrabFocus();
        }
    }

    protected abstract void BuildUi();

    protected virtual void OnSubmenuOpened() { }
    protected virtual void OnSubmenuClosed() { }

    private static void InjectTimelineBackground(Node parentLayer)
    {
        try
        {
            var scenePath = SceneHelper.GetScenePath("timeline_screen/timeline_screen");
            var scene = PreloadManager.Cache.GetScene(scenePath);

            var rawInstance = scene.Instantiate();
            var nativeId = rawInstance.GetInstanceId();
            rawInstance.Set("script", default); 

            if (InstanceFromId(nativeId) is not Control bgInstance) return;

            bgInstance.MouseFilter = Control.MouseFilterEnum.Ignore;
            bgInstance.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

            string[] uiNodeNames = ["EpochSlots", "EpochReminderText", "ReminderVfxHolder", "InputBlocker", "SharedBackstop", "EpochInspectScreen", "Line", "LineContainer", "SlotsContainer", "BackButton"];
            foreach (var nodeName in uiNodeNames)
            {
                bgInstance.FindChild(nodeName, true, false)?.QueueFree();
            }

            parentLayer.AddChild(bgInstance);
            parentLayer.MoveChild(bgInstance, 0); 
        }
        catch (Exception) { }
    }

    private static PanelContainer CreateStyledPanelContainer(Vector2 minSize, Color bgColor, int padding)
    {
        var panel = new PanelContainer { CustomMinimumSize = minSize };
        var styleBox = new StyleBoxFlat {
            BgColor = bgColor,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
            ContentMarginLeft = padding,
            ContentMarginRight = padding,
            ContentMarginTop = padding,
            ContentMarginBottom = padding
        };
        panel.AddThemeStyleboxOverride("panel", styleBox);
        return panel;
    }

    protected static VBoxContainer CreateVBoxContainer(int separation = 25)
    {
        var vbox = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        vbox.SetAnchorsPreset(Control.LayoutPreset.FullRect); 
        vbox.AddThemeConstantOverride("separation", separation);
        return vbox;
    }

    protected static HBoxContainer CreateHBoxContainer(int separation = 40)
    {
        var hbox = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        hbox.AddThemeConstantOverride("separation", separation);
        return hbox;
    }

    protected static Label CreateTitleLabel(string text, int fontSize = 28)
    {
        var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        return label;
    }

    protected static SfxButton CreateButton(string text, Color baseColor, Vector2? minSize = null, int fontSize = 24)
    {
        var btn = new SfxButton { 
            Text = text, 
            CustomMinimumSize = minSize ?? new Vector2(120, 50) 
        };
        btn.AddThemeFontSizeOverride("font_size", fontSize);
        ApplyCustomButtonStyle(btn, baseColor);
        return btn;
    }

    private static void ApplyCustomButtonStyle(Button btn, Color baseColor)
    {
        var normalStyle = new StyleBoxFlat {
            BgColor = baseColor, 
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6
        };
        
        var hoverStyle = (StyleBoxFlat)normalStyle.Duplicate();
        hoverStyle.BgColor = baseColor.Lightened(0.15f);
        
        var pressedStyle = (StyleBoxFlat)normalStyle.Duplicate();
        pressedStyle.BgColor = baseColor.Darkened(0.15f);

        btn.AddThemeStyleboxOverride("normal", normalStyle);
        btn.AddThemeStyleboxOverride("hover", hoverStyle);
        btn.AddThemeStyleboxOverride("pressed", pressedStyle);
        btn.AddThemeStyleboxOverride("focus", hoverStyle); 
    }
}