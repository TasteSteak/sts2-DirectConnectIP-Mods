#nullable enable
using Godot;
using DirectConnectIP.Localization;
using DirectConnectIP.Helpers;

namespace DirectConnectIP.Screens;

public partial class ProfileSettingsScreen : DirectUiScreen
{
    private LineEdit _nameInput = null!;
    private LineEdit _idInput = null!;
    private CheckButton _offlineTakeoverToggle = null!;
    private CheckButton _androidCompatToggle = null!;
    private SfxButton _saveBtn = null!;

    protected override Vector2 PanelSize => new(500, 620);
    protected override int VBoxSeparation => 16;
    protected override Control InitialFocusedControl => _nameInput;
    protected override bool ShouldInjectBackground => false;

    protected override void BuildUi()
    {
        Vbox.AddChild(CreateTitleLabel(LocTexts.ProfileTitle, 30));
        Vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 5) }); 

        var nameBox = CreateInputGroup(LocTexts.ProfileNameLabel, out _nameInput);
        _nameInput.Text = ModEntry.Config.LocalPlayerName;
        _nameInput.MaxLength = 20;
        _nameInput.PlaceholderText = LocTexts.ProfileNameLabel;
        Vbox.AddChild(nameBox);

        var idBox = CreateInputGroup(LocTexts.ProfileIdLabel, out _idInput);
        _idInput.Text = ModEntry.Config.LocalPlayerId.ToString();
        _idInput.MaxLength = 18;
        _idInput.PlaceholderText = LocTexts.ProfileIdLabel;
        Vbox.AddChild(idBox);

        var restartLabel = new Label {
            Text = LocTexts.ProfileRestartSection,
            HorizontalAlignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(420, 24)
        };
        restartLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.78f, 0.35f));
        restartLabel.AddThemeFontSizeOverride("font_size", 16);
        Vbox.AddChild(restartLabel);

        _offlineTakeoverToggle = CreateSettingsToggle(LocTexts.ProfileOfflineTakeoverLabel, ModEntry.Config.EnableOfflineTakeover);
        _androidCompatToggle = CreateSettingsToggle(LocTexts.ProfileAndroidCompatLabel, ModEntry.Config.EnableAndroidCompatFix);
        Vbox.AddChild(_offlineTakeoverToggle);
        Vbox.AddChild(_androidCompatToggle);

        var warningLabel = new Label {
            Text = LocTexts.ProfileWarning,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(420, 55),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        warningLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.4f, 0.4f)); 
        warningLabel.AddThemeFontSizeOverride("font_size", 12);
        Vbox.AddChild(warningLabel);

        var btnsHBox = CreateHBoxContainer(30);
        Vbox.AddChild(btnsHBox);

        _saveBtn = CreateButton(LocTexts.BtnSave, new Color(0.2f, 0.55f, 0.25f), new Vector2(140, 45), 20);
        var cancelBtn = CreateButton(LocTexts.BtnReturn, new Color(0.4f, 0.4f, 0.45f), new Vector2(140, 45), 20);
        
        btnsHBox.AddChild(_saveBtn);
        btnsHBox.AddChild(cancelBtn);

        _saveBtn.Pressed += HandleSave;
        cancelBtn.Pressed += CloseScreen;
    }

    private void HandleSave()
    {
        var newName = _nameInput.Text.Trim();
        var newIdStr = _idInput.Text.Trim();

        if (string.IsNullOrEmpty(newName)) {
            _nameInput.Text = "";
            _nameInput.PlaceholderText = LocTexts.ErrEmptyName; 
            return; 
        }

        if (!ulong.TryParse(newIdStr, out var newId)) {
            _idInput.Text = "";
            _idInput.PlaceholderText = LocTexts.ErrInvalidId; 
            return; 
        }

        ModEntry.Config.UpdateSettings(newName, newId, _offlineTakeoverToggle.ButtonPressed, _androidCompatToggle.ButtonPressed);
        CloseScreen();
    }

    private static VBoxContainer CreateInputGroup(string labelText, out LineEdit input)
    {
        var group = new VBoxContainer();
        group.AddThemeConstantOverride("separation", 8);

        var label = new Label { 
            Text = labelText, 
            HorizontalAlignment = HorizontalAlignment.Left 
        };
        label.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f));
        label.AddThemeFontSizeOverride("font_size", 16);
        
        input = new LineEdit { 
            CustomMinimumSize = new Vector2(300, 45), 
            Alignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        input.AddThemeFontSizeOverride("font_size", 20);
        
        var style = new StyleBoxFlat { 
            BgColor = new Color(0.15f, 0.15f, 0.15f), 
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, 
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4 
        };
        input.AddThemeStyleboxOverride("normal", style);

        group.AddChild(label);
        group.AddChild(input);
        return group;
    }

    private static CheckButton CreateSettingsToggle(string text, bool enabled)
    {
        var toggle = new CheckButton {
            Text = text,
            ButtonPressed = enabled,
            CustomMinimumSize = new Vector2(420, 38),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        toggle.AddThemeFontSizeOverride("font_size", 16);
        return toggle;
    }
}
