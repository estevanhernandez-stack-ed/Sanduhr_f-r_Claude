using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>What the Launcher piece draws from: the buttons, which apps the Start menu no longer has, and the pictures
/// and names the shell and the images folder already gave. A missing picture draws a placeholder.</summary>
/// <param name="Model">The buttons and their orientation.</param>
/// <param name="Missing">App IDs the Start menu no longer lists; their buttons draw dimmed.</param>
/// <param name="AppIcons">Each app's own icon, by ID.</param>
/// <param name="AppNames">Each app's Start-menu name, by ID (the name a screen reader says when a button has no label).</param>
/// <param name="Images">The chosen PNGs, by stored file name.</param>
internal sealed record DeskLauncherFrame(
    LauncherModel Model,
    IReadOnlySet<string> Missing,
    IReadOnlyDictionary<string, ImageSource> AppIcons,
    IReadOnlyDictionary<string, string> AppNames,
    IReadOnlyDictionary<string, ImageSource> Images)
{
    public static DeskLauncherFrame Of(LauncherModel model) => new(
        model, new HashSet<string>(), new Dictionary<string, ImageSource>(), new Dictionary<string, string>(), new Dictionary<string, ImageSource>());
}

/// <summary>The Launcher piece: a canvas of buttons laid out by <see cref="LauncherLayout"/>. DeskClicks finds it by its buttons.</summary>
internal sealed class LauncherPieceVisual : Canvas
{
    public IReadOnlyList<LauncherButtonVisual> Buttons { get; init; } = [];

    protected override AutomationPeer OnCreateAutomationPeer() => new LauncherPiecePeer(this);

    private sealed class LauncherPiecePeer(LauncherPieceVisual owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => "Launcher";
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
        protected override string GetNameCore() => "Launcher";
        protected override bool IsControlElementCore() => true;
    }
}

/// <summary>
/// One Launcher button as drawn: its image (the app's icon, a chosen PNG, or the link glyph), an optional label under
/// it, a glow halo that shows on hover and touch, and a press dip. Its frame carries a faint plate (alpha 1 of 255) so
/// the whole frame takes the mouse while the pixels between buttons pass clicks to the desktop. A button for an app
/// that is gone draws dimmed. Screen readers see it as a button named by its label or the app's name, and can invoke it.
/// </summary>
internal sealed class LauncherButtonVisual : Border
{
    private static readonly Brush Plate = Frozen(Color.FromArgb(1, 0, 0, 0));
    private readonly Border _halo;
    private readonly FrameworkElement _imageHost;
    private readonly ScaleTransform _dip = new(1, 1);
    private readonly bool _animate;

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public LauncherButtonVisual(int index, LauncherButton button, string name, bool missing, LauncherSlot slot, FrameworkElement image,
        UIElement? label, Color glow)
    {
        Index = index;
        Button = button;
        DisplayName = name;
        Missing = missing;
        _animate = SystemParameters.ClientAreaAnimation;
        Width = slot.Frame.Width;
        Height = slot.Frame.Height;
        Background = Plate;
        Opacity = missing ? 0.38 : 1;
        Cursor = System.Windows.Input.Cursors.Hand;
        Focusable = false;

        AutomationProperties.SetName(this, missing ? $"{name}, not installed" : name);
        AutomationProperties.SetHelpText(this, missing
            ? "This app is no longer installed. Choose it to open Launcher settings."
            : button.Kind == LauncherKind.Link ? "Opens a link in your browser." : "Opens this app.");

        double side = slot.Image.Width;
        var grid = new Grid { Width = slot.Frame.Width, Height = slot.Frame.Height, IsHitTestVisible = false };

        // The halo is the image's size scaled up around its center, so it reaches past the image without
        // changing the button's layout size; the reach stops short of the Desk window's padding.
        double reach = Math.Min(14, side * 0.25);
        double grow = (side + 2 * reach) / side;
        var brush = new RadialGradientBrush { RadiusX = 0.5, RadiusY = 0.5, Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5) };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x80, glow.R, glow.G, glow.B), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x55, glow.R, glow.G, glow.B), 0.62));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, glow.R, glow.G, glow.B), 1));
        brush.Freeze();
        _halo = new Border
        {
            Width = side, Height = side, Background = brush, CornerRadius = new CornerRadius(side * 0.3),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new ScaleTransform(grow, grow),
            Opacity = 0, IsHitTestVisible = false,
        };
        grid.Children.Add(_halo);

        _imageHost = new Grid
        {
            Width = side, Height = side, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = _dip,
        };
        ((Grid)_imageHost).Children.Add(image);
        grid.Children.Add(_imageHost);

        if (label is not null)
        {
            if (label is FrameworkElement fe)
            {
                fe.VerticalAlignment = VerticalAlignment.Top;
                fe.Margin = new Thickness(0, slot.Label.Top - slot.Frame.Top, 0, 0);
            }
            grid.Children.Add(label);
        }
        Child = grid;
    }

    public int Index { get; }
    public LauncherButton Button { get; }
    public string DisplayName { get; }
    public bool Missing { get; }

    /// <summary>Raised when a screen reader (or a test) invokes the button.</summary>
    public event Action? Invoked;

    public bool IsGlowing => _glowTarget;
    public bool IsDipped => _dipTarget;
    private bool _glowTarget, _dipTarget;

    /// <summary>The halo on or off: it fades over about a tenth of a second, or snaps when Windows animations are off.</summary>
    public void SetGlow(bool on)
    {
        if (_glowTarget == on) return;
        _glowTarget = on;
        Fade(_halo, UIElement.OpacityProperty, on ? 1 : 0, 0.12);
    }

    /// <summary>The press dip: the image shrinks a little while pressed or touched, and springs back.</summary>
    public void SetDip(bool on)
    {
        if (_dipTarget == on) return;
        _dipTarget = on;
        double to = on ? 0.9 : 1;
        if (!_animate)
        {
            _dip.ScaleX = _dip.ScaleY = to;
            return;
        }
        var a = new DoubleAnimation(to, TimeSpan.FromSeconds(on ? 0.06 : 0.12)) { FillBehavior = FillBehavior.HoldEnd };
        Timeline.SetDesiredFrameRate(a, 30);
        _dip.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        _dip.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    private void Fade(UIElement target, DependencyProperty property, double to, double seconds)
    {
        if (!_animate)
        {
            target.BeginAnimation(property, null);
            target.SetValue(property, to);
            return;
        }
        var a = new DoubleAnimation(to, TimeSpan.FromSeconds(seconds)) { FillBehavior = FillBehavior.HoldEnd };
        Timeline.SetDesiredFrameRate(a, 30);
        target.BeginAnimation(property, a);
    }

    internal void RaiseInvoked() => Invoked?.Invoke();

    protected override AutomationPeer OnCreateAutomationPeer() => new LauncherButtonPeer(this);

    private sealed class LauncherButtonPeer(LauncherButtonVisual owner) : FrameworkElementAutomationPeer(owner), IInvokeProvider
    {
        protected override string GetClassNameCore() => "LauncherButton";
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
        protected override string GetNameCore() => AutomationProperties.GetName(owner);
        protected override string GetHelpTextCore() => AutomationProperties.GetHelpText(owner);
        protected override bool IsControlElementCore() => true;
        protected override bool IsKeyboardFocusableCore() => false;

        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Invoke ? this : base.GetPattern(patternInterface);

        public void Invoke() => owner.Dispatcher.BeginInvoke(owner.RaiseInvoked);
    }
}

/// <summary>Builds the Launcher piece (spec, Desk Launcher) from a frame, in the Desk's font and ink.</summary>
internal static class DeskLauncherPiece
{
    /// <summary>The Launcher piece, or null with no buttons.</summary>
    /// <param name="frame">The buttons and what they draw from.</param>
    /// <param name="size">The piece's type size: the Desk's clock size times the piece's own scale.</param>
    /// <param name="family">The Desk font.</param>
    /// <param name="ink">The Desk ink as a brush (labels, the link glyph).</param>
    /// <param name="inkColor">The first ink color: the default halo.</param>
    /// <param name="textOpacity">The ink's text opacity.</param>
    /// <param name="shadow">The label's shadow, or null.</param>
    public static LauncherPieceVisual? Create(DeskLauncherFrame frame, double size, FontFamily family, Brush ink, Color inkColor,
        double textOpacity, Effect? shadow)
    {
        var model = frame.Model;
        if (model.IsEmpty) return null;
        bool anyLabel = model.Buttons.Any(b => b.Label is not null);
        var labels = model.Buttons.Select(b => b.Label).OfType<string>().ToList();
        var geometry = Fit(model, size, family, labels);
        double lineHeight = LauncherLayout.LabelLineHeight(geometry.LabelSize);
        var buttons = new List<LauncherButtonVisual>(model.Buttons.Count);
        for (int i = 0; i < model.Buttons.Count; i++)
        {
            var b = model.Buttons[i];
            var slot = geometry.Slots[i];
            frame.AppNames.TryGetValue(b.Target, out var appName);
            string name = b.DisplayName(b.Kind == LauncherKind.App ? appName : null);
            bool missing = b.Kind == LauncherKind.App && frame.Missing.Contains(b.Target);
            var image = Picture(frame, b, slot.Image.Width, name, family, ink, textOpacity);
            UIElement? label = null;
            if (anyLabel && b.Label is { } text)
            {
                var block = Label(text, family, geometry.LabelSize, lineHeight);
                block.Foreground = ink;
                block.Opacity = textOpacity;
                block.Effect = shadow;
                block.MaxHeight = slot.Label.Height;
                label = block;
            }
            var glow = b.Glow is { } hex ? DeskView.Rgb(hex) : inkColor;
            buttons.Add(new LauncherButtonVisual(i, b, name, missing, slot, image, label, glow));
        }

        var piece = new LauncherPieceVisual { Width = geometry.Width, Height = geometry.Height, Buttons = buttons };
        AutomationProperties.SetName(piece, "Launcher");
        for (int i = 0; i < buttons.Count; i++)
        {
            Canvas.SetLeft(buttons[i], geometry.Slots[i].Frame.X);
            Canvas.SetTop(buttons[i], geometry.Slots[i].Frame.Y);
            piece.Children.Add(buttons[i]);
        }
        return piece;
    }

    /// <summary>A label: centered, wrapping under its button, an ellipsis only past the label row's lines.</summary>
    internal static TextBlock Label(string text, FontFamily family, double fontSize, double lineHeight) => new()
    {
        Text = text, FontFamily = family, FontSize = fontSize,
        TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch,
        TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
        LineHeight = lineHeight, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        IsHitTestVisible = false,
    };

    /// <summary>How many lines <paramref name="text"/> wraps to at <paramref name="width"/>, not capped.</summary>
    internal static int LabelLines(string text, FontFamily family, double fontSize, double lineHeight, double width)
    {
        if (width <= 0 || lineHeight <= 0) return 1;
        var block = Label(text, family, fontSize, lineHeight);
        block.Measure(new Size(width, double.PositiveInfinity));
        return Math.Max(1, (int)Math.Round(block.DesiredSize.Height / lineHeight));
    }

    /// <summary>
    /// The layout whose label row holds every label whole (#318: the limit is 24 characters, and one line under a
    /// button held about ten). The smallest that fits, in this order: one line at the usual width; two lines; two lines
    /// with the buttons widened step by step up to <see cref="LauncherLayout.MaxLabelledWidthShare"/>. A label still too
    /// long there (24 of the widest letters) ends in an ellipsis on its second line.
    /// </summary>
    internal static LauncherGeometry Fit(LauncherModel model, double size, FontFamily family, IReadOnlyList<string> labels)
    {
        int count = model.Buttons.Count;
        var geometry = LauncherLayout.Compute(count, model.Orientation, size, labels.Count > 0);
        if (labels.Count == 0 || geometry.Slots.Count == 0) return geometry;
        double lineHeight = LauncherLayout.LabelLineHeight(geometry.LabelSize);
        int Needed(double width) => labels.Max(t => LabelLines(t, family, geometry.LabelSize, lineHeight, width));

        int lines = Needed(geometry.Slots[0].Label.Width);
        if (lines <= 1) return geometry;
        for (double share = LauncherLayout.LabelledWidthShare; ; share = Math.Min(share + 0.1, LauncherLayout.MaxLabelledWidthShare))
        {
            var wider = LauncherLayout.Compute(count, model.Orientation, size, true, LauncherLayout.MaxLabelLines, share);
            if (Needed(wider.Slots[0].Label.Width) <= LauncherLayout.MaxLabelLines || share >= LauncherLayout.MaxLabelledWidthShare)
                return wider;
        }
    }

    /// <summary>The button's picture: the chosen PNG, else the app's own icon, else a link glyph or a placeholder.</summary>
    private static FrameworkElement Picture(DeskLauncherFrame frame, LauncherButton b, double side, string name, FontFamily family, Brush ink, double textOpacity)
    {
        ImageSource? source = null;
        if (b.Image is { } file) frame.Images.TryGetValue(file, out source);
        if (source is null && b.Kind == LauncherKind.App && frame.AppIcons.TryGetValue(b.Target, out var icon)) source = icon;
        if (source is not null)
        {
            var img = new Image { Source = source, Width = side, Height = side, Stretch = Stretch.Uniform, IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            return img;
        }
        if (b.Kind == LauncherKind.Link) return LinkGlyph(side, ink, textOpacity);

        // The app's icon has not arrived (or the shell has none): a quiet tile with its first letter.
        var tile = new Grid { Width = side, Height = side, IsHitTestVisible = false };
        tile.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(side * 0.22), BorderBrush = ink, BorderThickness = new Thickness(Math.Max(1.5, side * 0.04)),
            Opacity = 0.45 * textOpacity,
        });
        var initial = name.Length > 0 ? name[..1].ToUpperInvariant() : "?";
        tile.Children.Add(new TextBlock
        {
            Text = initial, FontFamily = family, FontSize = side * 0.5, Foreground = ink, Opacity = 0.7 * textOpacity,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        });
        return tile;
    }

    /// <summary>A globe drawn in the ink: what a link button shows until its owner picks an image.</summary>
    private static FrameworkElement LinkGlyph(double side, Brush ink, double textOpacity)
    {
        double c = side / 2, r = side * 0.38;
        var g = new GeometryGroup();
        g.Children.Add(new EllipseGeometry(new Point(c, c), r, r));
        g.Children.Add(new EllipseGeometry(new Point(c, c), r * 0.45, r));
        g.Children.Add(new LineGeometry(new Point(c - r, c), new Point(c + r, c)));
        g.Children.Add(new LineGeometry(new Point(c, c - r), new Point(c, c + r)));
        double dx = r * 0.87, dy = r * 0.5;
        g.Children.Add(new LineGeometry(new Point(c - dx, c - dy), new Point(c + dx, c - dy)));
        g.Children.Add(new LineGeometry(new Point(c - dx, c + dy), new Point(c + dx, c + dy)));
        g.Freeze();
        return new Path
        {
            Data = g, Stroke = ink, StrokeThickness = Math.Max(1.6, side * 0.045), Width = side, Height = side,
            Opacity = 0.9 * textOpacity, IsHitTestVisible = false, Stretch = Stretch.None,
        };
    }
}
