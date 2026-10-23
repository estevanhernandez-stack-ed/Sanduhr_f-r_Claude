namespace Sanduhr.Core;

/// <summary>One button's rectangles, in the Launcher piece's own coordinates (top-left origin).</summary>
/// <param name="Frame">The whole button: the hit region (its image and its label row).</param>
/// <param name="Image">Where the image draws, a square centered at the top of the frame.</param>
/// <param name="Label">The label row under the image; empty when no button has a label.</param>
public readonly record struct LauncherSlot(DeskRect Frame, DeskRect Image, DeskRect Label);

/// <summary>The Launcher piece laid out: each button's slot, the piece's size and the type size of a label.</summary>
public sealed record LauncherGeometry(IReadOnlyList<LauncherSlot> Slots, double Width, double Height, double LabelSize, double ImageSize)
{
    /// <summary>The piece's whole area.</summary>
    public DeskRect Bounds => new(0, 0, Width, Height);
}

/// <summary>
/// Where the Launcher's buttons sit for a row or a column at a size, and which button a point is on. Pure
/// arithmetic, so the Desk draws from it and the click rule (<see cref="DeskHitTest"/>) can be tested
/// against it without a window. All lengths are DIPs; <c>size</c> is the piece's type size, the Desk's clock
/// size times the piece's own scale, so a Launcher scales with the same steps as every other piece.
/// </summary>
public static class LauncherLayout
{
    /// <summary>The image's side as a share of the size.</summary>
    public const double ImageShare = 0.55;

    /// <summary>The label's type size as a share of the size.</summary>
    public const double LabelShare = 0.15;

    /// <summary>The smallest label type size, so a label stays readable at the smallest scale.</summary>
    public const double MinLabelSize = 11;

    /// <summary>The gap between two buttons as a share of the image's side.</summary>
    public const double GapShare = 0.28;

    /// <summary>The gap between an image and its label as a share of the image's side.</summary>
    public const double LabelGapShare = 0.08;

    /// <summary>With labels, a button is this many images wide so a short label fits under it.</summary>
    public const double LabelledWidthShare = 1.7;

    /// <summary>
    /// A label wraps to at most this many lines under its button, so the label <see cref="LauncherButton.MaxLabel"/>
    /// allows draws whole: on one line a label was cut at about ten characters ("YouTube…", #318).
    /// </summary>
    public const int MaxLabelLines = 2;

    /// <summary>The widest a labelled button gets, in images, when its label needs the room: two lines at this width hold
    /// the <see cref="LauncherButton.MaxLabel"/> characters of an ordinary name in the Desk's fonts.</summary>
    public const double MaxLabelledWidthShare = 2.6;

    /// <summary>The height of one label line at a label type size.</summary>
    public static double LabelLineHeight(double labelSize) => Math.Ceiling(labelSize * 1.35);

    /// <summary>The image's side at a size.</summary>
    public static double ImageSide(double size) => Math.Max(8, Finite(size) * ImageShare);

    /// <summary>
    /// The layout of <paramref name="count"/> buttons (0 to <see cref="LauncherModel.MaxButtons"/>; more is cut
    /// to that). A row runs left to right, a column top to bottom; every button has the same size, with a label
    /// row on every one when <paramref name="anyLabel"/> (so the images line up whether or not each has a label).
    /// The label row is <paramref name="labelLines"/> lines tall (1 to <see cref="MaxLabelLines"/>) and a labelled button
    /// <paramref name="labelledWidth"/> images wide (<see cref="LabelledWidthShare"/> to <see cref="MaxLabelledWidthShare"/>):
    /// what the longest label needs as the Desk measures it in its font, so a Launcher of short labels keeps its size.
    /// </summary>
    public static LauncherGeometry Compute(int count, LauncherOrientation orientation, double size, bool anyLabel,
        int labelLines = 1, double labelledWidth = LabelledWidthShare)
    {
        count = Math.Clamp(count, 0, LauncherModel.MaxButtons);
        double image = ImageSide(size);
        double gap = image * GapShare;
        double labelSize = Math.Max(MinLabelSize, Finite(size) * LabelShare);
        double labelGap = anyLabel ? image * LabelGapShare : 0;
        double labelHeight = anyLabel ? LabelLineHeight(labelSize) * Math.Clamp(labelLines, 1, MaxLabelLines) : 0;
        double share = double.IsFinite(labelledWidth) ? Math.Clamp(labelledWidth, LabelledWidthShare, MaxLabelledWidthShare) : LabelledWidthShare;
        double width = anyLabel ? image * share : image;
        double height = image + labelGap + labelHeight;

        var slots = new List<LauncherSlot>(count);
        for (int i = 0; i < count; i++)
        {
            double x = orientation == LauncherOrientation.Row ? i * (width + gap) : 0;
            double y = orientation == LauncherOrientation.Column ? i * (height + gap) : 0;
            var frame = new DeskRect(x, y, width, height);
            var img = new DeskRect(x + (width - image) / 2, y, image, image);
            var label = anyLabel ? new DeskRect(x, y + image + labelGap, width, labelHeight) : new DeskRect(x, y + image, width, 0);
            slots.Add(new LauncherSlot(frame, img, label));
        }
        if (count == 0) return new LauncherGeometry(slots, 0, 0, labelSize, image);
        double totalW = orientation == LauncherOrientation.Row ? count * width + (count - 1) * gap : width;
        double totalH = orientation == LauncherOrientation.Column ? count * height + (count - 1) * gap : height;
        return new LauncherGeometry(slots, totalW, totalH, labelSize, image);
    }

    /// <summary>The button the point is on, by its frame; null in the gaps between buttons and outside the piece.</summary>
    public static int? HitTest(LauncherGeometry geometry, double x, double y)
    {
        for (int i = 0; i < geometry.Slots.Count; i++)
        {
            var f = geometry.Slots[i].Frame;
            if (x >= f.Left && x < f.Right && y >= f.Top && y < f.Bottom) return i;
        }
        return null;
    }

    private static double Finite(double v) => double.IsFinite(v) && v > 0 ? v : 0;
}
