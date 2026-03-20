using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace DirectConnectIP.Helpers;

public static class ModUiHelper
{
    private const string SubmenuButtonPrefabPath = "res://scenes/ui/submenu_button.tscn";

    private static PackedScene _cachedButtonScene;
    private static readonly Dictionary<string, Texture2D> TextureCache = new();

    private static PackedScene GetButtonScene()
    {
        _cachedButtonScene ??= GD.Load<PackedScene>(SubmenuButtonPrefabPath);
        return _cachedButtonScene;
    }

    private static Texture2D GetOrLoadTexture(string customIconFileName)
    {
        if (string.IsNullOrEmpty(customIconFileName)) return null;

        if (TextureCache.TryGetValue(customIconFileName, out var cachedTex))
            return cachedTex;

        Texture2D loadedTexture = null;
        var resPath = customIconFileName.StartsWith("res://") ? customIconFileName : "res://" + customIconFileName;

        try
        {
            if (ResourceLoader.Exists(resPath))
            {
                loadedTexture = GD.Load<Texture2D>(resPath);
            }
            else if (FileAccess.FileExists(resPath))
            {
                var buffer = FileAccess.GetFileAsBytes(resPath);
                var img = new Image();
                if (img.LoadPngFromBuffer(buffer) == Error.Ok)
                {
                    loadedTexture = ImageTexture.CreateFromImage(img);
                }
            }
        }
        catch (Exception ex) 
        { 
            GD.PrintErr($"[DirectConnectIP] 图标加载异常: {ex.Message}"); 
        }

        if (loadedTexture != null)
        {
            TextureCache[customIconFileName] = loadedTexture;
        }

        return loadedTexture;
    }

    public static NSubmenuButton CreateCustomSubmenuButton(
        string newName,
        string locKeyPrefix,
        Color bgColor,
        Action<NButton> onClickAction,
        string customIconFileName = null
    ) 
    {
        var newBtn = GetButtonScene().Instantiate<NSubmenuButton>();
        newBtn.Name = newName;
        
        newBtn.CustomMinimumSize = new Vector2(330, 705);

        if (onClickAction != null)
        {
            newBtn.Connect(NClickableControl.SignalName.Released, Callable.From(onClickAction));
        }

        CustomizeAppearance(newBtn, bgColor, customIconFileName);
        newBtn.Connect(Node.SignalName.Ready, Callable.From(() => newBtn.SetIconAndLocalization(locKeyPrefix)));

        return newBtn;
    }

    public static NSubmenuButton CreateModeToggleButton(
        string newName, 
        string newTitle, 
        Color bgColor, 
        Action<NButton> onClickAction) 
    {
        var newBtn = GetButtonScene().Instantiate<NSubmenuButton>();
        newBtn.Name = newName;
        
        newBtn.CustomMinimumSize = new Vector2(330, 705);

        if (onClickAction != null)
        {
            newBtn.Connect(NClickableControl.SignalName.Released, Callable.From(onClickAction));
        }

        HideNodeByName(newBtn, "Title");
        HideNodeByName(newBtn, "Description");
        HideNodeByName(newBtn, "Icon");

        if (newBtn.FindChild("Lock", true, false) is TextureRect lockIcon)
        {
            lockIcon.Modulate = Colors.Transparent;
        }

        var customLabel = new Label 
        { 
            Text = newTitle,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        customLabel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        customLabel.AddThemeFontSizeOverride("font_size", 30);
        customLabel.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.5f));
        customLabel.AddThemeConstantOverride("shadow_offset_y", 2);
        newBtn.AddChild(customLabel);

        CustomizeAppearance(newBtn, bgColor, null);

        return newBtn;
    }

    private static void HideNodeByName(Node parent, string nodeName)
    {
        if (parent.FindChild(nodeName, true, false) is Control controlNode)
        {
            controlNode.Visible = false;
        }
    }

    private static void CustomizeAppearance(NSubmenuButton btn, Color bgColor, string customIconFileName)
    {
        if (btn.FindChild("BgPanel", true, false) is Control { Material: ShaderMaterial oldMaterial } bgPanel)
        {
            bgPanel.Material = (ShaderMaterial)oldMaterial.Duplicate();
            bgPanel.SelfModulate = bgColor;
        }

        if (btn.FindChild("Icon", true, false) is not TextureRect iconRect ||
            string.IsNullOrEmpty(customIconFileName)) return;
        
        var tex = GetOrLoadTexture(customIconFileName);
        if (tex != null)
        {
            iconRect.Texture = tex;
        }
    }
}