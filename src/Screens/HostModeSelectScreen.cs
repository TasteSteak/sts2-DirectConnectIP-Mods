#nullable enable
using System;
using DirectConnectIP.Helpers;
using DirectConnectIP.Localization;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace DirectConnectIP.Screens;

public partial class HostModeSelectScreen(Action onModeSelectedAndContinue) : DirectUiScreen
{
    private SfxButton _cancelBtn = null!;

    protected override Vector2 PanelSize => new(480, 450);
    protected override int VBoxSeparation => 20;
    protected override Control InitialFocusedControl => _cancelBtn;

    protected override void BuildUi()
    {
        var headerBox = new HBoxContainer { CustomMinimumSize = new Vector2(420, 40) };
        headerBox.AddChild(new Control { CustomMinimumSize = new Vector2(40, 40) });

        var titleLabel = CreateTitleLabel(LocTexts.TitleHostMode);
        titleLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        titleLabel.HorizontalAlignment = HorizontalAlignment.Center; 
        headerBox.AddChild(titleLabel);

        var faqBtn = CreateButton("?", new Color(0.25f, 0.45f, 0.65f), new Vector2(40, 40), 18);
        faqBtn.TooltipText = LocTexts.TooltipFaq;
        faqBtn.Pressed += () => new FaqScreen().OpenScreen(GetParent(), closeOthers: false);
        headerBox.AddChild(faqBtn);
            
        Vbox.AddChild(headerBox);

        var btnContainer = CreateVBoxContainer(20);
        Vbox.AddChild(btnContainer);

        var steamPlatform = ModEntry.Config.IsSteamAvailable;
        var steamBtn = ModUiHelper.CreateModeToggleButton(
            "BtnSteam",
            steamPlatform ? LocTexts.BtnSteam : $"{LocTexts.BtnSteam} (N/A)", 
            new Color(0.2f, 0.4f, 0.7f), 
            _ =>
            {
                if (!steamPlatform) return;
                HostModeSettings.CurrentMode = HostMode.Steam; 
                onModeSelectedAndContinue.Invoke();
                Callable.From(CloseScreen).CallDeferred();
            }
        );

        if (!steamPlatform) 
        {
            Callable.From(steamBtn.Disable).CallDeferred();
            steamBtn.TooltipText = LocTexts.ErrSteam; 
        }
        ApplySafeSizeFlags(steamBtn);

        var enetBtn = ModUiHelper.CreateModeToggleButton(
            "BtnENet", LocTexts.BtnEnet, 
            new Color(0.0f, 0.65f, 0.85f), 
            _ => {
                HostModeSettings.CurrentMode = HostMode.ENet; 
                onModeSelectedAndContinue.Invoke(); 
                Callable.From(CloseScreen).CallDeferred();
            }
        );
        ApplySafeSizeFlags(enetBtn);

        btnContainer.AddChild(steamBtn);
        btnContainer.AddChild(enetBtn);
            
        Vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });
            
        var settingsBtn = CreateButton(LocTexts.BtnProfile, new Color(0.3f, 0.3f, 0.35f), new Vector2(320, 45), 18);
        settingsBtn.Pressed += () => new ProfileSettingsScreen().OpenScreen(GetParent(), closeOthers: false);
        Vbox.AddChild(settingsBtn);

        _cancelBtn = CreateButton(LocTexts.BtnCancel, new Color(0.4f, 0.4f, 0.45f), new Vector2(140, 45), 20);
        _cancelBtn.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        _cancelBtn.Pressed += CloseScreen;
        Vbox.AddChild(_cancelBtn);
    }
    
    private static void ApplySafeSizeFlags(NSubmenuButton btn)
    {
        btn.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        btn.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        btn.CustomMinimumSize = new Vector2(300, 60); 
    }
}