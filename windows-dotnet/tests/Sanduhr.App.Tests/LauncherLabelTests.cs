using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Sanduhr.App.Desk;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// #318: Settings and propose_desk accept a Launcher label of 24 characters, and under the icon it was cut at about ten
/// ("YouTube…"). A label now wraps to two lines and the buttons widen, within a max, until every label draws whole.
/// Measured in the Desk's own fonts, through the piece the Desk draws.
/// </summary>
public class LauncherLabelTests
{
    private static readonly string[] Fonts = ["EsteFont Pro", "EsteFont 26", ""];

    /// <summary>Ordinary names cut to the 24-character limit, as Settings stores them.</summary>
    private static readonly string[] LongLabels =
    [
        "YouTube Music", "Visual Studio Code Insid", "Microsoft Teams classic", "Adobe Photoshop Lightroo",
        "Abcdefghijklmnopqrstuvwx", "Spotify Premium Family 2",
    ];

    private static LauncherButton App(string id, string? label) => LauncherButton.Create(LauncherKind.App, id, null, label, null)!;

    private static LauncherPieceVisual Piece(LauncherModel model, double size, FontFamily family)
    {
        var frame = new DeskLauncherFrame(model, new HashSet<string>(), new Dictionary<string, ImageSource>(), new Dictionary<string, string>(), new Dictionary<string, ImageSource>());
        return DeskLauncherPiece.Create(frame, size, family, Brushes.White, Colors.White, 1, null)!;
    }

    private static IEnumerable<TextBlock> Labels(DependencyObject root)
    {
        if (root is TextBlock t) yield return t;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var found in Labels(child)) yield return found;
    }

    [Fact]
    public void A_label_up_to_the_limit_draws_whole_in_every_Desk_font() => AppHost.Run(() =>
    {
        var cut = new List<string>();
        foreach (var font in Fonts)
        {
            var family = new DeskFonts(["Segoe UI"]).Families(font).Regular;
            foreach (var scale in new[] { 0.8, 1.0, 1.1, 1.6 })
            {
                var model = new LauncherModel(LauncherOrientation.Row,
                    [.. LongLabels.Select((l, i) => App("App" + i, l)).Take(LauncherModel.MaxButtons)]);
                double size = DeskSettings.Default.TimeSize * scale;
                var piece = Piece(model, size, family);
                var geometry = DeskLauncherPiece.Fit(model, size, family, LongLabels);
                Assert.Equal(geometry.Width, piece.Width, 3);  // the piece is drawn at the fitted layout
                foreach (var (button, label) in piece.Buttons.Select(b => (b, Labels(b).Single(t => t.TextWrapping == TextWrapping.Wrap))))
                {
                    Assert.Equal(button.Button.Label, label.Text);  // the whole text is there, never cut short
                    Assert.Equal(geometry.Slots[button.Index].Label.Height, label.MaxHeight, 3);
                    // Unclipped, in the label's own font, size and line height, at the label row's width.
                    int lines = DeskLauncherPiece.LabelLines(label.Text, label.FontFamily, label.FontSize, label.LineHeight, geometry.Slots[button.Index].Label.Width);
                    if (lines * label.LineHeight > label.MaxHeight + 0.5)
                        cut.Add($"{(font.Length == 0 ? "system" : font)} at {scale}: {label.Text}");
                }
            }
        }
        Assert.True(cut.Count == 0, "cut: " + string.Join("; ", cut));
    });

    [Fact]
    public void YouTube_Music_wraps_under_its_button_instead_of_YouTube_ellipsis() => AppHost.Run(() =>
    {
        var family = new DeskFonts(["Segoe UI"]).Families("EsteFont 26").Regular;
        var piece = Piece(new LauncherModel(LauncherOrientation.Row, [App("Chrome._crx_ytm", "YouTube Music")]), DeskSettings.Default.TimeSize * 1.1, family);
        var label = Labels(piece.Buttons[0]).Single(t => t.Text == "YouTube Music");
        double line = LauncherLayout.LabelLineHeight(label.FontSize);

        Assert.Equal(TextWrapping.Wrap, label.TextWrapping);
        Assert.Equal(2 * line, label.MaxHeight, 3);
    });

    [Fact]
    public void Short_labels_keep_the_one_line_layout() => AppHost.Run(() =>
    {
        double size = DeskSettings.Default.TimeSize;
        var model = new LauncherModel(LauncherOrientation.Row, [App("A", "Mail"), App("B", null), App("C", "Code")]);
        var piece = Piece(model, size, new DeskFonts(["Segoe UI"]).Families("EsteFont Pro").Regular);
        var usual = LauncherLayout.Compute(3, LauncherOrientation.Row, size, anyLabel: true);

        Assert.Equal(usual.Width, piece.Width, 3);
        Assert.Equal(usual.Height, piece.Height, 3);
    });
}
