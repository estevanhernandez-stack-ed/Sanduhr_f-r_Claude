using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>
/// A chat's drawer settings (look and placement spec, section 3): the look and opacity are saved and, with an arrangement, the drawers line
/// up from the chat's corner and direction (a part the chat left out comes from the user's own Tidy) with the result and Undo on the drawers
/// bar. Undo puts back the places Tidy wrote and the look and opacity from before. Refused with the busy line while a review or a result
/// bar is up, and nothing applies partly: every check runs before the first save. Moves panels only, never a file.
/// </summary>
internal static class ChatDrawerSettings
{
    /// <summary>Null when it was done, otherwise the line to show (nothing applied).</summary>
    public static string? Apply(PastedDrawerLook look, DrawerService drawers, DeskController desk, DrawerReviewService review)
    {
        if (desk.TidyBusy() || review.Reviewing || review.BarOpen) return DrawerReview.Busy;
        var before = drawers.Settings;
        DrawerSettings WithLook(DrawerSettings s) => s with { Look = look.Look ?? s.Look, Opacity = look.Opacity ?? s.Opacity };
        if (look.Arrange is not { } wanted)
        {
            drawers.Save(WithLook(before));
            return null;
        }
        if (!before.Enabled) return DrawerReview.DrawersOff;
        if (!desk.HasScreens) return SettingsNames.DrawersSortUnavailable;
        var tidy = new DrawerTidy(look.HasCorner ? wanted.Corner : before.Tidy.Corner, look.HasFlow ? wanted.Flow : before.Tidy.Flow);
        // Tidy goes first: it refuses (nothing written) while a review is open, so a refusal leaves the look as it was too.
        if (desk.Tidy(tidy, null) is not { } r) return DrawerReview.Busy;
        bool lookSaved = look.Look is not null || look.Opacity is not null;
        if (lookSaved) drawers.Save(WithLook(drawers.Settings));
        string tidyLine = r.Moved == 0 ? DrawerReview.TidyNothing : DrawerReview.Tidied(r.Moved);
        return review.ShowResult(SetupCopy.ChatDrawerSettingsResult(lookSaved, tidyLine), () =>
        {
            desk.RestorePlaces(r.Changes);
            if (lookSaved) drawers.Save(drawers.Settings with { Look = before.Look, Opacity = before.Opacity });
        });
    }
}
