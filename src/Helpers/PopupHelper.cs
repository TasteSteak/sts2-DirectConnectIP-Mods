using MegaCrit.Sts2.Core.Nodes.CommonUi;
using DirectConnectIP.Localization;
using MegaCrit.Sts2.Core.Entities.Multiplayer;

namespace DirectConnectIP.Helpers;

public static class PopupHelper
{
    public static void ShowNetError(NetErrorInfo info)
    {
        if (info.GetReason() == NetError.Kicked)
        {
            ShowPopup(LocTexts.ErrIdCollisionTitle, LocTexts.ErrIdCollisionDesc);
            return;
        }
        
        var popup = NErrorPopup.Create(info);
        if (popup != null)
        {
            NModalContainer.Instance!.Add(popup);
        }
    }

    private static void ShowPopup(string title, string body, bool showReportBug = false)
    {
        var popup = NErrorPopup.Create(title, body, showReportBugButton: showReportBug);
        if (popup != null)
        {
            NModalContainer.Instance!.Add(popup);
        }
    }
}