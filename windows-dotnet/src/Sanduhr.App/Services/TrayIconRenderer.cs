using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Sanduhr.App.Services;

/// <summary>
/// Renders the live tray glyph: the highest tier's utilization percentage
/// painted into a 32x32 icon, color-ramped by the same usage thresholds the
/// cards use. The hourglass identity rides in the tray <c>ToolTipText</c>
/// ("Sanduhr - hourglass NN%") since a glyph + two digits both legible at 16px
/// is a losing fight — the number is the signal, so the number gets the pixels.
/// </summary>
[SupportedOSPlatform("windows")]
public static class TrayIconRenderer
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    /// <summary>
    /// Build an icon showing <paramref name="percent"/> (clamped 0-100), tinted
    /// by the usage ramp. <paramref name="percent"/> &lt; 0 renders a neutral
    /// dot (no data yet). The returned <see cref="Icon"/> owns its handle and
    /// must be disposed by the caller when it swaps in the next one.
    /// </summary>
    public static Icon Render(int percent)
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            Color tint = percent < 0 ? Color.FromArgb(0x77, 0x77, 0x77) : Ramp(percent);

            // Soft rounded plate so the number reads against any taskbar color.
            using (var plate = new SolidBrush(Color.FromArgb(210, 18, 18, 20)))
            using (var path = RoundedRect(new Rectangle(1, 1, size - 2, size - 2), 7))
            {
                g.FillPath(plate, path);
                using var pen = new Pen(Color.FromArgb(160, tint), 1.5f);
                g.DrawPath(pen, path);
            }

            string text = percent < 0 ? "--" : Math.Clamp(percent, 0, 100).ToString();
            float emSize = text.Length >= 3 ? 13f : 17f; // "100" needs to shrink
            using var font = new Font("Segoe UI", emSize, FontStyle.Bold, GraphicsUnit.Pixel);
            using var fmt = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            using var textBrush = new SolidBrush(tint);
            g.DrawString(text, font, textBrush, new RectangleF(0, 0, size, size), fmt);
        }

        IntPtr hicon = bmp.GetHicon();
        try
        {
            using var shared = Icon.FromHandle(hicon);
            return (Icon)shared.Clone();
        }
        finally
        {
            DestroyIcon(hicon);
        }
    }

    private static Color Ramp(int pct)
    {
        if (pct < 50) return Color.FromArgb(0x4a, 0xde, 0x80);
        if (pct < 75) return Color.FromArgb(0xfa, 0xcc, 0x15);
        if (pct < 90) return Color.FromArgb(0xfb, 0x92, 0x3c);
        return Color.FromArgb(0xf8, 0x71, 0x71);
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
