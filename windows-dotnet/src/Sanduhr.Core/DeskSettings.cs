namespace Sanduhr.Core;

/// <summary>The Desk's edge margins in DIPs (settings.json <c>desk_margins</c>); top is measured
/// below the taskbar when it sits on top, as the Mac measures below the menu bar.</summary>
public readonly record struct DeskMarginSettings(double Left, double Right, double Top, double Bottom)
{
    /// <summary>The Mac's defaults: 52 at the sides, 40 at the top, 60 at the bottom.</summary>
    public static DeskMarginSettings Default { get; } = new(52, 52, 40, 60);

    /// <summary>Each side kept within 0 to <see cref="DeskSettings.MaxMargin"/>; anything unreadable is the default.</summary>
    public DeskMarginSettings Clamped() => new(
        DeskSettings.ClampMargin(Left, Default.Left), DeskSettings.ClampMargin(Right, Default.Right),
        DeskSettings.ClampMargin(Top, Default.Top), DeskSettings.ClampMargin(Bottom, Default.Bottom));
}

/// <summary>
/// The Desk's settings (spec 5.1): <c>desk_enabled</c> (off), <c>desk_layout</c> (the layout string,
/// <see cref="DeskArrangement.Standard"/>), <c>desk_margins</c>, <c>desk_font</c> (null = EsteFont
/// Pro, "" = the system font) and <c>desk_time_size</c> (the clock's size; every piece but a message
/// scales from it), and <c>desk_plate</c> (<see cref="DeskPlate"/>, off). A missing or unreadable key is its default, so an existing install reads unchanged
/// and the Desk stays off until switched on.
/// </summary>
public sealed record DeskSettings(bool Enabled, string Layout, DeskMarginSettings Margins, string? Font, double TimeSize, bool Plate = false)
{
    public const double DefaultTimeSize = 112;
    public const double MinTimeSize = 48;
    public const double MaxTimeSize = 200;
    public const double MaxMargin = 400;

    public static DeskSettings Default { get; } =
        new(false, DeskArrangement.Standard, DeskMarginSettings.Default, null, DefaultTimeSize);

    /// <summary>The clock size kept within <see cref="MinTimeSize"/> to <see cref="MaxTimeSize"/>;
    /// anything unreadable is <see cref="DefaultTimeSize"/>.</summary>
    public static double ClampTimeSize(double size) =>
        double.IsFinite(size) && size > 0 ? Math.Clamp(Math.Round(size), MinTimeSize, MaxTimeSize) : DefaultTimeSize;

    /// <summary>A margin kept within 0 to <see cref="MaxMargin"/>; anything unreadable is <paramref name="fallback"/>.</summary>
    public static double ClampMargin(double value, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(Math.Round(value), 0, MaxMargin) : fallback;

    /// <summary>This, with every value kept in range and the layout written back in its canonical form.</summary>
    public DeskSettings Normalized() => this with
    {
        Layout = new DeskArrangement(Layout).Layout,
        Margins = Margins.Clamped(),
        TimeSize = ClampTimeSize(TimeSize),
    };
}
