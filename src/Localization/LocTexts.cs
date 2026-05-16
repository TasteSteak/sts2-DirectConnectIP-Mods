using MegaCrit.Sts2.Core.Localization;

namespace DirectConnectIP.Localization;

public static class LocTexts
{
    private static string GetLocText(string key)
    {
        return LocManager.Instance.GetTable("main_menu_ui").GetRawText(key);
    }

    public static string FaqTitle => GetLocText("MOD_DC_FAQ_TITLE");
    public static string FaqBtnClose => GetLocText("MOD_DC_FAQ_BTN_CLOSE");
    public static string FaqQ1 => GetLocText("MOD_DC_FAQ_Q1");
    public static string FaqA1 => GetLocText("MOD_DC_FAQ_A1");
    public static string FaqQ2 => GetLocText("MOD_DC_FAQ_Q2");
    public static string FaqA2 => GetLocText("MOD_DC_FAQ_A2");
    public static string FaqQ3 => GetLocText("MOD_DC_FAQ_Q3");
    public static string FaqA3 => GetLocText("MOD_DC_FAQ_A3");
    public static string FaqQ4 => GetLocText("MOD_DC_FAQ_Q4");
    public static string FaqA4 => GetLocText("MOD_DC_FAQ_A4");
    public static string FaqQ5 => GetLocText("MOD_DC_FAQ_Q5");
    public static string FaqA5 => GetLocText("MOD_DC_FAQ_A5");
    public static string FaqQ6 => GetLocText("MOD_DC_FAQ_Q6");
    public static string FaqA6 => GetLocText("MOD_DC_FAQ_A6");

    public static string TitleHostMode => GetLocText("MOD_DC_TITLE_HOST_MODE");
    public static string TooltipFaq => GetLocText("MOD_DC_TOOLTIP_FAQ");
    public static string BtnSteam => GetLocText("MOD_DC_BTN_STEAM");
    public static string BtnEnet => GetLocText("MOD_DC_BTN_ENET");
    public static string ErrSteam => GetLocText("MOD_DC_ERR_STEAM");
    public static string BtnProfile => GetLocText("MOD_DC_BTN_PROFILE");
    public static string BtnCancel => GetLocText("MOD_DC_BTN_CANCEL");

    public static string TitleJoinServer => GetLocText("MOD_DC_TITLE_JOIN_SERVER");
    public static string PlaceholderIp => GetLocText("MOD_DC_PLACEHOLDER_IP");
    public static string LabelRecent => GetLocText("MOD_DC_LABEL_RECENT");
    public static string BtnConnect => GetLocText("MOD_DC_BTN_CONNECT");
    public static string ErrIp => GetLocText("MOD_DC_ERR_IP");

    public static string LoadingConnecting => GetLocText("MOD_DC_LOADING_CONNECTING");
    public static string BtnCancelConnection => GetLocText("MOD_DC_BTN_CANCEL_CONNECTION");

    public static string ProfileTitle => GetLocText("MOD_DC_PROFILE_TITLE");
    public static string ProfileNameLabel => GetLocText("MOD_DC_PROFILE_NAME_LABEL");
    public static string ProfileIdLabel => GetLocText("MOD_DC_PROFILE_ID_LABEL");
    public static string ProfileWarning => GetLocText("MOD_DC_PROFILE_WARNING");
    public static string ProfileRestartSection => GetLocText("MOD_DC_PROFILE_RESTART_SECTION");
    public static string ProfileOfflineTakeoverLabel => GetLocText("MOD_DC_PROFILE_OFFLINE_TAKEOVER_LABEL");
    public static string ProfileAndroidCompatLabel => GetLocText("MOD_DC_PROFILE_ANDROID_COMPAT_LABEL");
    public static string BtnSave => GetLocText("MOD_DC_BTN_SAVE");
    public static string BtnReturn => GetLocText("MOD_DC_BTN_RETURN");
    public static string ErrEmptyName => GetLocText("MOD_DC_ERR_EMPTY_NAME");
    public static string ErrInvalidId => GetLocText("MOD_DC_ERR_INVALID_ID");
    
    public static string ErrIdCollisionTitle => GetLocText("MOD_DC_ERR_ID_COLLISION_TITLE");
    public static string ErrIdCollisionDesc => GetLocText("MOD_DC_ERR_ID_COLLISION_DESC");
}
