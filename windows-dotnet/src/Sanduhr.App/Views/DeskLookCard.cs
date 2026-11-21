using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Sanduhr.App.Desk;
using Sanduhr.Core;

namespace Sanduhr.App.Views;

/// <summary>
/// The top of a ready-made look's card (5.0 spec 5, section 6): the user's wallpaper, or its first color, with the look's arrangement
/// over it in the fitted look's ink. The look's name stands where the look puts its clock, in the look's font, with its glow when it
/// has one; every other shown piece is a short bar; a plated look gets a soft plate behind each stack. Placed with the same Core
/// call the layout map uses (<see cref="DeskGeometry.Point"/>), for one screen. It suggests the look; the real Desk is the preview.
/// Decorative: the button around it carries the accessible name.
/// </summary>
internal sealed class DeskLookCard : Canvas
{
    public const double CardWidth = 208;
    public const double CardHeight = 117;

    private const double NameSize = 17;
    private const double BarWidth = 34, BarHeight = 3.5, Gap = 3;

    /// <summary>The fonts the cards draw with (tests set it); null reads this PC's, once.</summary>
    internal static DeskFonts? FontsForTests { get; set; }

    private static readonly Lazy<DeskFonts> PcFonts = new(() => DeskFonts.ForThisPc(new Paths()));

    public static readonly DependencyProperty DesignProperty = Register<DeskDesign?>(nameof(Design), null);
    public static readonly DependencyProperty WallpaperProperty = Register<ImageSource?>(nameof(Wallpaper), null);
    public static readonly DependencyProperty BackdropHexProperty = Register(nameof(BackdropHex), "");
    public static readonly DependencyProperty BrightnessProperty = Register<double?>(nameof(Brightness), null);
    public static readonly DependencyProperty LookNameProperty = Register(nameof(LookName), "");

    private static DependencyProperty Register<T>(string name, T initial) => DependencyProperty.Register(
        name, typeof(T), typeof(DeskLookCard), new PropertyMetadata(initial, (d, _) => ((DeskLookCard)d).Redraw()));

    public DeskDesign? Design { get => (DeskDesign?)GetValue(DesignProperty); set => SetValue(DesignProperty, value); }
    public ImageSource? Wallpaper { get => (ImageSource?)GetValue(WallpaperProperty); set => SetValue(WallpaperProperty, value); }
    public string BackdropHex { get => (string)GetValue(BackdropHexProperty); set => SetValue(BackdropHexProperty, value); }
    public double? Brightness { get => (double?)GetValue(BrightnessProperty); set => SetValue(BrightnessProperty, value); }
    public string LookName { get => (string)GetValue(LookNameProperty); set => SetValue(LookNameProperty, value); }

    public DeskLookCard()
    {
        Width = CardWidth;
        Height = CardHeight;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        Focusable = false;
        IsHitTestVisible = false;   // the button takes the click
        Redraw();
    }

    private void Redraw()
    {
        Children.Clear();
        Children.Add(Backdrop());
        if (Design is not { Layout: { } layout } design) return;

        // The ink as the Desk would resolve it over this wallpaper: automatic ink follows the brightness.
        var ink = DeskInk.Resolve(DeskDesignApply.Ink(DeskInkSettings.Default, design), null, Brightness);
        var brush = DeskView.InkBrush(ink.Stops);
        var content = new DeskRect(CardWidth * 0.07, CardHeight * 0.09, CardWidth * 0.86, CardHeight * 0.82);
        var shown = layout.Where(p => !p.IsHidden && p.Anchor is not null).ToList();
        bool clockShown = shown.Any(p => p.Piece == "clock");

        foreach (var stack in shown.GroupBy(p => p.Anchor!.Value))
        {
            var marks = stack.Select(p => Mark(p, design, ink, brush)).ToList();
            foreach (var m in marks) m.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double height = marks.Sum(m => m.DesiredSize.Height) + Gap * (marks.Count - 1);
            double width = marks.Max(m => m.DesiredSize.Width);
            var (px, py) = DeskGeometry.Point(stack.Key, content);
            double top = DeskAnchors.Row(stack.Key) switch
            {
                DeskRow.Top => py,
                DeskRow.Middle => py - height / 2,
                _ => py - height,
            };
            double Left(double w) => Math.Clamp(DeskAnchors.Column(stack.Key) switch
            {
                DeskColumn.Left => px,
                DeskColumn.Center => px - w / 2,
                _ => px - w,
            }, 0, Math.Max(0, CardWidth - w));
            top = Math.Clamp(top, 0, Math.Max(0, CardHeight - height));

            if (design.Plate == true)
            {
                var plate = new Rectangle
                {
                    Width = width + 8, Height = height + 6, RadiusX = 4, RadiusY = 4, Tag = "plate",
                    Fill = new SolidColorBrush(DeskView.Rgb(DeskPlate.ColorHex(ink.DarkInk))) { Opacity = DeskPlate.Opacity },
                };
                SetLeft(plate, Left(width) - 4);
                SetTop(plate, top - 3);
                Children.Add(plate);
            }
            foreach (var m in marks)
            {
                SetLeft(m, Left(m.DesiredSize.Width));
                SetTop(m, top);
                Children.Add(m);
                top += m.DesiredSize.Height + Gap;
            }
        }

        if (!clockShown)
        {
            // A look without a clock: its name in the middle.
            var name = NameMark(1, design, ink, brush);
            name.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            SetLeft(name, (CardWidth - name.DesiredSize.Width) / 2);
            SetTop(name, (CardHeight - name.DesiredSize.Height) / 2);
            Children.Add(name);
        }
    }

    private FrameworkElement Backdrop()
    {
        if (Wallpaper is { } picture)
            return new Image { Source = picture, Width = CardWidth, Height = CardHeight, Stretch = Stretch.UniformToFill, Tag = "backdrop" };
        var fill = new Rectangle { Width = CardWidth, Height = CardHeight, Tag = "backdrop" };
        if (InkHex.Normalize(BackdropHex) is { } hex) fill.Fill = new SolidColorBrush(DeskView.Rgb(hex));
        else fill.SetResourceReference(Shape.FillProperty, "Sanduhr.Brush.Bg");
        return fill;
    }

    private FrameworkElement Mark(DeskPieceLayout p, DeskDesign design, ResolvedInk ink, Brush brush)
    {
        double size = p.Size ?? 1;
        if (p.Piece == "clock") return NameMark(size, design, ink, brush);
        var bar = new Rectangle
        {
            Width = BarWidth * size, Height = BarHeight * size, RadiusX = BarHeight * size / 2, RadiusY = BarHeight * size / 2,
            Fill = brush, Opacity = 0.85 * ink.TextOpacity, Tag = "piece",
        };
        bar.Effect = Lit(ink);
        return bar;
    }

    private TextBlock NameMark(double size, DeskDesign design, ResolvedInk ink, Brush brush)
    {
        var fonts = FontsForTests ?? PcFonts.Value;
        var (_, bold) = fonts.Families(design.Font is { } f ? DeskDesignApply.FontSetting(f) : null);
        return new TextBlock
        {
            Text = LookName,   // streamer: notname - a built-in look's name, fixed copy
            FontFamily = bold,
            FontWeight = FontWeights.Bold,
            FontSize = NameSize * size,
            Foreground = brush,
            Opacity = ink.TextOpacity,
            Effect = Lit(ink),
        };
    }

    /// <summary>The look's light at card size: its glow in the ink's middle color, or a small shadow; neither when it has neither.</summary>
    private static Effect? Lit(ResolvedInk ink)
    {
        if (ink.Glow > 0)
            return new DropShadowEffect
            {
                Color = DeskView.Rgb(DeskGlowLayer.Middle(ink.Stops)), ShadowDepth = 0,
                BlurRadius = 3 + 7 * ink.Glow, Opacity = DeskGlow.Opacity(ink.Glow),
            };
        if (ink.ShadowOpacity > 0)
            return new DropShadowEffect { Color = DeskView.Rgb(ink.ShadowHex), ShadowDepth = 1, BlurRadius = 3, Direction = 270, Opacity = ink.ShadowOpacity };
        return null;
    }
}
