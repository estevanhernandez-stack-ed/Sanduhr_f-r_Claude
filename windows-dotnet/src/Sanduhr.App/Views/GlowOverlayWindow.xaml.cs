using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Sanduhr.App.Interop;
using Sanduhr.Core;

namespace Sanduhr.App.Views;

/// <summary>
/// The Claude Code edge light: borderless, click-through, never activated, never in the taskbar. One
/// instance is reused; <see cref="Glow"/> places it in device pixels, fades it in over 150 ms, holds,
/// fades out over about 1.5 s total, and hides it.
/// </summary>
internal partial class GlowOverlayWindow : Window
{
    public static readonly TimeSpan FadeIn = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan Hold = TimeSpan.FromMilliseconds(850);
    public static readonly TimeSpan Total = TimeSpan.FromMilliseconds(1500);

    private int _run;

    public GlowOverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => MakeClickThrough();
    }

    private void MakeClickThrough()
    {
        var h = new WindowInteropHelper(this).Handle;
        int ex = GlowNative.GetWindowLong(h, GlowNative.GWL_EXSTYLE);
        GlowNative.SetWindowLong(h, GlowNative.GWL_EXSTYLE,
            ex | GlowNative.WS_EX_TRANSPARENT | GlowNative.WS_EX_LAYERED
               | GlowNative.WS_EX_NOACTIVATE | GlowNative.WS_EX_TOOLWINDOW);
    }

    /// <summary>Show the glow at <paramref name="rect"/> (device pixels) in <paramref name="color"/>.</summary>
    public void Glow(PxRect rect, Color color, GlowEdgeSide side = GlowEdgeSide.Top)
    {
        int run = ++_run;
        Light.SetSide(side);
        Light.SetColor(color);
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        if (!IsVisible) Show();
        var h = new WindowInteropHelper(this).Handle;
        // Twice: the first move can land on a monitor with another DPI, which rescales the window.
        for (int i = 0; i < 2; i++)
            GlowNative.SetWindowPos(h, GlowNative.HWND_TOPMOST, rect.X, rect.Y, rect.Width, rect.Height,
                GlowNative.SWP_NOACTIVATE | GlowNative.SWP_NOOWNERZORDER | GlowNative.SWP_SHOWWINDOW);

        var a = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(FadeIn)));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(FadeIn + Hold)));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(Total)));
        a.Completed += (_, _) =>
        {
            if (run != _run) return; // a newer glow owns the window
            Opacity = 0;
            Hide();
        };
        BeginAnimation(OpacityProperty, a);
    }

    /// <summary>Hide now, cancelling any glow in flight.</summary>
    public void Dismiss()
    {
        _run++;
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        if (IsVisible) Hide();
    }
}
