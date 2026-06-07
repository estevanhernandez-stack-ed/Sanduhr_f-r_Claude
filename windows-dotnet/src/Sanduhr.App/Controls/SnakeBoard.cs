using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Sanduhr.App.Theming;
using Sanduhr.Core;

namespace Sanduhr.App.Controls;

/// <summary>
/// The WPF view for the cooldown snake — ports <c>game.py</c>'s <c>paintEvent</c>.
/// The game LOGIC is the pure <see cref="SnakeGame"/>; this draws the dimmed
/// board, grid border, score/best, the snake (rounded cells with a fading tail),
/// the food, and the GAME OVER overlay, all in the active theme.
/// </summary>
public sealed class SnakeBoard : FrameworkElement
{
    /// <summary>The pure game model; the overlay calls
    /// <see cref="FrameworkElement.InvalidateVisual"/> each tick.</summary>
    public SnakeGame? Game { get; set; }

    /// <summary>Active palette for board, snake (accent), and text.</summary>
    public ThemePalette Palette { get; set; } = ThemePalette.Obsidian;

    protected override void OnRender(DrawingContext dc)
    {
        var game = Game;
        if (game is null)
            return;

        double w = ActualWidth, h = ActualHeight;
        if (w < 40 || h < 40)
            return;

        // Dim backdrop (ported: bg @ alpha 230).
        var dim = new SolidColorBrush(WithAlpha(Palette.Bg, 230 / 255.0));
        dim.Freeze();
        dc.DrawRectangle(dim, null, new Rect(0, 0, w, h));

        double box = Math.Min(w, h) - 20;
        double cell = box / game.GridSize;
        double offX = (w - box) / 2;
        double offY = (h - box) / 2;

        // Grid border.
        var borderPen = new Pen(new SolidColorBrush(Palette.Border), 2);
        borderPen.Freeze();
        dc.DrawRectangle(null, borderPen, new Rect(offX, offY, box, box));

        // Score (left) + best (right) above the board.
        DrawText(dc, $"Score: {game.Score}", Palette.TextDim, 12, FontWeights.Bold,
            new Point(offX, offY - 20));
        DrawTextRight(dc, $"Best: {game.HighScore}", Palette.TextDim, 12, FontWeights.Bold,
            offX + box, offY - 20);

        // Snake — rounded cells, tail alpha-fades out (ported).
        int count = game.Snake.Count;
        for (int i = 0; i < count; i++)
        {
            var (sx, sy) = game.Snake[i];
            double alpha = Math.Max(100, 255 - (i * 255 / Math.Max(count, 1))) / 255.0;
            var brush = new SolidColorBrush(WithAlpha(Palette.Accent, alpha));
            brush.Freeze();
            double px = offX + sx * cell;
            double py = offY + sy * cell;
            dc.DrawRoundedRectangle(brush, null,
                new Rect(px + 1, py + 1, cell - 2, cell - 2), 2, 2);
        }

        // Food — a soft ink dot.
        var foodBrush = new SolidColorBrush(Palette.Text);
        foodBrush.Freeze();
        double fx = offX + game.Food.X * cell;
        double fy = offY + game.Food.Y * cell;
        double fr = (cell - 4) / 2.0;
        dc.DrawEllipse(foodBrush, null, new Point(fx + cell / 2.0, fy + cell / 2.0), fr, fr);

        if (game.GameOver)
        {
            var scrim = new SolidColorBrush(WithAlpha(Palette.Bg, 0.55));
            scrim.Freeze();
            dc.DrawRectangle(scrim, null, new Rect(0, 0, w, h));
            DrawCentered(dc, "GAME OVER\nPress Space to Restart\nEsc to Exit",
                Palette.Text, 16, FontWeights.Bold, w, h);
        }
    }

    private void DrawText(DrawingContext dc, string text, Color color, double size, FontWeight weight, Point at)
    {
        var ft = MakeText(text, color, size, weight);
        dc.DrawText(ft, new Point(at.X, at.Y - ft.Height));
    }

    private void DrawTextRight(DrawingContext dc, string text, Color color, double size, FontWeight weight, double right, double bottom)
    {
        var ft = MakeText(text, color, size, weight);
        dc.DrawText(ft, new Point(right - ft.Width, bottom - ft.Height));
    }

    private void DrawCentered(DrawingContext dc, string text, Color color, double size, FontWeight weight, double w, double h)
    {
        var ft = MakeText(text, color, size, weight);
        ft.TextAlignment = TextAlignment.Center;
        ft.MaxTextWidth = w;
        dc.DrawText(ft, new Point(0, (h - ft.Height) / 2.0));
    }

    private FormattedText MakeText(string text, Color color, double size, FontWeight weight)
        => new(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI"),
                FontStyles.Normal, weight, FontStretches.Normal),
            size,
            new SolidColorBrush(color),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static Color WithAlpha(Color c, double alpha)
        => Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), c.R, c.G, c.B);
}
