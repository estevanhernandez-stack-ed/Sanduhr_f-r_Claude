namespace Sanduhr.Core;

/// <summary>"A nicer desktop" off (setup spec, section 1): the Desk draws only drawers. The saved layout keeps every piece, so ticking
/// desktop again puts each one back where it was.</summary>
public static class SetupDesk
{
    public static bool Shows(string widget, bool desktop) => desktop || DrawerPieces.IsWord(widget);

    public static string Layout(string layout, bool desktop)
    {
        if (desktop) return layout;
        var arrangement = new DeskArrangement(layout);
        return new DeskArrangement(arrangement.Pieces.Where(p => DrawerPieces.IsWord(p.Widget))).Layout;
    }
}
