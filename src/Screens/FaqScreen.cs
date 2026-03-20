#nullable enable
using DirectConnectIP.Helpers;
using Godot;
using DirectConnectIP.Localization;

namespace DirectConnectIP.Screens;

public partial class FaqScreen : DirectUiScreen
{
    private SfxButton _closeBtn = null!;

    protected override Vector2 PanelSize => new(550, 600);
    protected override int VBoxSeparation => 15;
    protected override Control InitialFocusedControl => _closeBtn;
    protected override bool ShouldInjectBackground => false;

    protected override void BuildUi()
    {
        var titleLabel = CreateTitleLabel(LocTexts.FaqTitle);
        titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        Vbox.AddChild(titleLabel);

        var scroll = new ScrollContainer {
            CustomMinimumSize = new Vector2(490, 420),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        Vbox.AddChild(scroll);

        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 20);
        content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(content);

        AddFaqItem(content, LocTexts.FaqQ1, LocTexts.FaqA1);
        AddFaqItem(content, LocTexts.FaqQ2, LocTexts.FaqA2);
        AddFaqItem(content, LocTexts.FaqQ3, LocTexts.FaqA3);
        AddFaqItem(content, LocTexts.FaqQ4, LocTexts.FaqA4);
        AddFaqItem(content, LocTexts.FaqQ5, LocTexts.FaqA5);
        AddFaqItem(content, LocTexts.FaqQ6, LocTexts.FaqA6);

        _closeBtn = CreateButton(LocTexts.FaqBtnClose, new Color(0.35f, 0.35f, 0.4f), new Vector2(160, 45), 18);
        _closeBtn.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        _closeBtn.Pressed += CloseScreen; 
        
        Vbox.AddChild(_closeBtn);
    }

    private static void AddFaqItem(VBoxContainer parent, string questionText, string answerText)
    {
        var itemBox = new VBoxContainer();
        itemBox.AddThemeConstantOverride("separation", 5);

        var q = new Label { 
            Text = questionText, 
            AutowrapMode = TextServer.AutowrapMode.WordSmart 
        };
        q.AddThemeColorOverride("font_color", new Color(0.9f, 0.75f, 0.3f));
        q.AddThemeFontSizeOverride("font_size", 16);
            
        var a = new Label { 
            Text = answerText, 
            AutowrapMode = TextServer.AutowrapMode.WordSmart 
        };
        a.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.85f));
        a.AddThemeFontSizeOverride("font_size", 14);

        itemBox.AddChild(q);
        itemBox.AddChild(a);
        parent.AddChild(itemBox);
    }
}