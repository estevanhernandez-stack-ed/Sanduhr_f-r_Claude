using System.Windows;
using Sanduhr.App.Theming;
using Sanduhr.App.Views;

namespace Sanduhr.App.Tests;

/// <summary>
/// The rolling calendar on a Settings page that is not showing has no layout (its width and height are 0), and a theme change
/// redraws it anyway (Put my Desk back crashed the widget with "Width and Height must be non-negative"). It must tolerate every
/// size it can be given: none, zero, or smaller than its own gaps.
/// </summary>
public sealed class CcCalendarControlTests
{
    private static readonly IReadOnlyDictionary<DateOnly, long> Days = new Dictionary<DateOnly, long>
    {
        [DateOnly.FromDateTime(DateTime.Now)] = 1200,
        [DateOnly.FromDateTime(DateTime.Now.AddDays(-3))] = 50,
    };

    private static void SetData(CcCalendarControl c) =>
        c.SetData(Days, new HashSet<DateOnly>(), DateOnly.FromDateTime(DateTime.Now.AddDays(-30)), ThemePalette.Obsidian);

    [Fact]
    public void SetDataOnAControlThatWasNeverLaidOutDoesNotThrow() =>
        Sta.Run(() =>
        {
            var calendar = new CcCalendarControl();
            Assert.Equal(0, calendar.ActualWidth);
            SetData(calendar);
            SetData(calendar);   // again, as a second theme change does
            Assert.Null(calendar.ToolTip);
        });

    [Fact]
    public void SetDataOnAControlWithWidthZeroDoesNotThrow() =>
        Sta.Run(() =>
        {
            var calendar = new CcCalendarControl { Width = 0 };
            SetData(calendar);
            calendar.Measure(new Size(300, 120));
            calendar.Arrange(new Rect(0, 0, 0, 120));
            calendar.UpdateLayout();
            SetData(calendar);
        });

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 96)]
    [InlineData(10, 96)]      // narrower than its own column gaps
    [InlineData(300, 10)]     // shorter than its header band
    [InlineData(300, 96)]
    public void EverySizeDrawsAndHitTestsWithoutThrowing(double w, double h) =>
        Sta.Run(() =>
        {
            var calendar = new CcCalendarControl { Width = w, Height = h, MinHeight = 0 };
            SetData(calendar);
            calendar.Measure(new Size(w, h));
            calendar.Arrange(new Rect(0, 0, w, h));
            calendar.UpdateLayout();   // draws
            SetData(calendar);         // the tooltip pass, laid out
            var window = new Window { Content = calendar, Width = 500, Height = 400, ShowActivated = false, WindowStyle = WindowStyle.None };
            window.Left = -20000;
            window.Show();
            try { SetData(calendar); }
            finally { window.Close(); }
        });
}
