using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Sanduhr.Core;

// The manual sign-in key window (manual-key): closed, then with the cf_clearance panel and the sessionKey steps both
// open, PNGs written to <out>/manual-key-*.png with the window and content heights in the log. The persist delegate
// is a stub that throws, so Save could not write a credential even if something clicked it. Off screen, no
// activation, no network, no credentials. Not part of all.
//
//   --scenario manual-key
public static partial class Program
{
    static async Task RenderManualKey(Assembly asm)
    {
        var type = asm.GetType("Sanduhr.App.Views.ManualKeyWindow")!;
        Func<string, CapturedCookies, string> persist = (_, _) => throw new InvalidOperationException("the harness never saves a key");
        var w = (Window)Activator.CreateInstance(type,
            BindingFlags.Public | BindingFlags.Instance, null, [ "Personal", persist, true ], null)!;
        OffScreen(w);
        w.Show();
        w.Left = -20000;
        w.Top = -20000;
        await Settle();

        void Snap(string state)
        {
            // The window's content has a 24 DIP margin, which Save would crop; the first visual child is the whole client area.
            var client = (FrameworkElement)System.Windows.Media.VisualTreeHelper.GetChild(w, 0);
            Save(client, $"manual-key-{state}");
            var content = (FrameworkElement)w.Content;
            Log.Add($"manual-key {state}: window {w.ActualWidth:0}x{w.ActualHeight:0}, client {client.ActualWidth:0}x{client.ActualHeight:0}, content (inside its 24 margin) {content.ActualWidth:0}x{content.ActualHeight:0}");
        }

        Snap("closed");
        ((UIElement)w.FindName("CloudflarePanel")).Visibility = Visibility.Visible;
        ((UIElement)w.FindName("HelpSteps")).Visibility = Visibility.Visible;
        await Settle();
        Snap("both-open");
        w.Close();
        await Settle();
    }
}
