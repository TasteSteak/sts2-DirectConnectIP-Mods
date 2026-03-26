#nullable enable
using System;
using DirectConnectIP.Helpers;
using DirectConnectIP.Localization;
using Godot;

namespace DirectConnectIP.Screens;

public partial class JoinServerScreen(Func<string, bool> onConnectSubmit) : DirectUiScreen
{
    private LineEdit _ipInput = null!;

    protected override Vector2 PanelSize => new(
        580, 
        Math.Min(800, 360f + ModEntry.Config.RecentServers.Count * 55f)
    );
    
    protected override Control InitialFocusedControl => _ipInput;

    protected override void BuildUi()
    {
        var titleLabel = CreateTitleLabel(LocTexts.TitleJoinServer);
        titleLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        titleLabel.CustomMinimumSize = new Vector2(0, 40);
        Vbox.AddChild(titleLabel);

        _ipInput = CreateIpInput();
        _ipInput.PlaceholderText = LocTexts.PlaceholderIp;
        Vbox.AddChild(_ipInput);

        if (ModEntry.Config.RecentServers.Count > 0)
        {
            Vbox.AddChild(new Label { 
                Text = LocTexts.LabelRecent, 
                HorizontalAlignment = HorizontalAlignment.Left,
                SelfModulate = new Color(0.6f, 0.6f, 0.6f)
            });

            var historyContainer = new VBoxContainer();
            historyContainer.AddThemeConstantOverride("separation", 8); 
            Vbox.AddChild(historyContainer);

            foreach (var server in ModEntry.Config.RecentServers)
            {
                var histBtn = CreateHistoryButton(server);
                histBtn.Pressed += () => {
                    _ipInput.Text = server;
                    SubmitAction();
                };
                historyContainer.AddChild(histBtn);
            }
        }

        Vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 5) }); 

        var profileBtn = CreateButton(LocTexts.BtnProfile, new Color(0.25f, 0.25f, 0.3f), new Vector2(380, 40), 16);
        profileBtn.Pressed += () => new ProfileSettingsScreen().OpenScreen(GetParent(), closeOthers: false);
        Vbox.AddChild(profileBtn);

        var btnContainer = CreateHBoxContainer(30);
        btnContainer.Alignment = BoxContainer.AlignmentMode.Center;
        Vbox.AddChild(btnContainer);

        var connectBtn = CreateButton(LocTexts.BtnConnect, new Color(0.2f, 0.6f, 0.3f), new Vector2(150, 45));
        var cancelBtn = CreateButton(LocTexts.BtnCancel, new Color(0.4f, 0.4f, 0.45f), new Vector2(150, 45));
        
        btnContainer.AddChild(connectBtn);
        btnContainer.AddChild(cancelBtn);

        cancelBtn.Pressed += CloseScreen;
        connectBtn.Pressed += SubmitAction;
        _ipInput.TextSubmitted += _ => SubmitAction();
    }

    private void SubmitAction()
    {
        var txt = _ipInput.Text.Trim();
        if (string.IsNullOrEmpty(txt)) return;

        var isValid = onConnectSubmit.Invoke(txt);
        if (isValid) return;
        
        _ipInput.Text = "";
        _ipInput.PlaceholderText = LocTexts.ErrIp;
        _ipInput.AddThemeColorOverride("font_placeholder_color", new Color(1f, 0.4f, 0.4f));
    }

    private static SfxButton CreateHistoryButton(string text)
    {
        var btn = new SfxButton { 
            Text = text, 
            CustomMinimumSize = new Vector2(400, 45), 
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter 
        };
        btn.AddThemeFontSizeOverride("font_size", 20);

        var normalStyle = new StyleBoxFlat { 
            BgColor = new Color(0.18f, 0.18f, 0.22f), 
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, 
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6, 
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1, 
            BorderColor = new Color(0.3f, 0.3f, 0.35f) 
        };
        
        var hoverStyle = (StyleBoxFlat)normalStyle.Duplicate(); 
        hoverStyle.BgColor = new Color(0.25f, 0.25f, 0.3f); 
        hoverStyle.BorderColor = new Color(0.9f, 0.8f, 0.4f); 
            
        var pressedStyle = (StyleBoxFlat)normalStyle.Duplicate(); 
        pressedStyle.BgColor = new Color(0.12f, 0.12f, 0.15f);

        btn.AddThemeStyleboxOverride("normal", normalStyle);
        btn.AddThemeStyleboxOverride("hover", hoverStyle);
        btn.AddThemeStyleboxOverride("pressed", pressedStyle);
        btn.AddThemeStyleboxOverride("focus", hoverStyle);

        return btn;
    }

    private static LineEdit CreateIpInput()
    {
        var input = new LineEdit { 
            PlaceholderText = "127.0.0.1:33771", 
            Alignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(0, 50)
        };
        input.AddThemeFontSizeOverride("font_size", 24);
        return input;
    }
}