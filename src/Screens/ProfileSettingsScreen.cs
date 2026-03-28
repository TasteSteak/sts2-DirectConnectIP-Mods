#nullable enable
using Godot;
using DirectConnectIP.Localization;
using DirectConnectIP.Helpers;

namespace DirectConnectIP.Screens;

public partial class ProfileSettingsScreen : DirectUiScreen
{
    private LineEdit _nameInput = null!;
    private LineEdit _idInput = null!;
    private SfxButton _saveBtn = null!;

    protected override Vector2 PanelSize => new(460, 520);
    protected override int VBoxSeparation => 20;
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

        var warningLabel = new Label {
            Text = LocTexts.ProfileWarning,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(400, 60),
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

        ModEntry.Config.UpdateProfile(newName, newId);
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
}