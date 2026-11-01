namespace Sanduhr.Core;

/// <summary>A drawer rule said as a sentence (a person reads "Pictures go to Images", not "Images to Images"). One place for the Drawers
/// page's rule rows and their accessible names.</summary>
public static class DrawerRuleText
{
    /// <summary>"Screenshots go to Screenshots", "Files not opened in 30 days go to Older files", "Names with \"invoice\" go to Receipts".
    /// <paramref name="drawerName"/> is the drawer as the caller draws it (the page passes its streamer-masked name). Name rules quote
    /// the user's own text.</summary>
    public static string Sentence(DrawerRule rule, string drawerName) => Subject(rule) + " go to " + drawerName;

    /// <summary>What the rule picks out, as the start of the sentence.</summary>
    public static string Subject(DrawerRule rule) => rule.Kind switch
    {
        DrawerRuleKind.Screenshots => "Screenshots",
        DrawerRuleKind.Images => "Pictures",
        DrawerRuleKind.Documents => "Documents",
        DrawerRuleKind.Installers => "Installers",
        DrawerRuleKind.Archives => "Zip files",
        DrawerRuleKind.Shortcuts => "Shortcuts",
        DrawerRuleKind.OlderThan => $"Files not opened in {rule.Days} {(rule.Days == 1 ? "day" : "days")}",
        DrawerRuleKind.NameContains => $"Names with \"{rule.Text}\"",
        _ => $"Names starting with \"{rule.Text}\"",
    };
}
