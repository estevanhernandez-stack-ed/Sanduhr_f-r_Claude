using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;

namespace Sanduhr.App.Tests;

/// <summary>
/// DIAGNOSTIC for #313, never merged: on the hosted runner the items of a list get no automation names. This opens Settings,
/// Accounts in three window placements and reports, for every ItemsControl, the generator status, item and container counts
/// and the item peers' names. It always fails, so the report lands in the CI log.
/// </summary>
[Collection("SettingsWindow")]
public sealed class Diag313ListItemNamesTests : AppWindowFixture
{
    private enum Placement { OffScreen, OnScreenTransparent, OnScreenVisible }

    [Fact]
    public void Report() => AppHost.Run(() =>
    {
        var report = new StringBuilder();
        report.AppendLine($"#313 diag. GITHUB_ACTIONS={Environment.GetEnvironmentVariable("GITHUB_ACTIONS")} " +
                          $"UserInteractive={Environment.UserInteractive} Session={Environment.GetEnvironmentVariable("SESSIONNAME")} " +
                          $"Screen={SystemParameters.PrimaryScreenWidth}x{SystemParameters.PrimaryScreenHeight} " +
                          $"RenderTier={RenderCapability.Tier >> 16}");
        foreach (var placement in Enum.GetValues<Placement>())
            report.Append(Probe(placement));
        Assert.Fail(report.ToString());
    });

    private static string Probe(Placement placement)
    {
        var sb = new StringBuilder();
        var widget = DemoWidget();
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask);
        var window = new SettingsWindow(svm) { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
        switch (placement)
        {
            case Placement.OffScreen: window.Left = -20000; window.Top = -20000; break;
            case Placement.OnScreenTransparent: window.Left = 0; window.Top = 0; window.Opacity = 0; break;
            case Placement.OnScreenVisible: window.Left = 0; window.Top = 0; break;
        }
        window.Show();
        try
        {
            var tabs = (TabControl)window.FindName("Tabs");
            tabs.SelectedItem = tabs.Items.OfType<TabItem>().First(t => t.Tag?.ToString() == "Accounts");
            sb.AppendLine($"== {placement}: IsLoaded={window.IsLoaded} IsVisible={window.IsVisible} Actual={window.ActualWidth:0}x{window.ActualHeight:0}");
            foreach (var stage in new[] { "after Show", "after UpdateLayout+Idle", "after Render+Idle x3" })
            {
                if (stage == "after UpdateLayout+Idle") { window.UpdateLayout(); Idle(DispatcherPriorityIdle); }
                if (stage == "after Render+Idle x3") for (int i = 0; i < 3; i++) { Idle(System.Windows.Threading.DispatcherPriority.Render); Idle(DispatcherPriorityIdle); }
                sb.AppendLine($"  -- {stage}");
                foreach (var list in Lists(window))
                {
                    int containers = 0;
                    for (int i = 0; i < list.Items.Count; i++) if (list.ItemContainerGenerator.ContainerFromIndex(i) is not null) containers++;
                    var peer = UIElementAutomationPeer.CreatePeerForElement(list);
                    string names;
                    try
                    {
                        peer.ResetChildrenCache();
                        names = string.Join(" | ", (peer.GetChildren() ?? []).Select(p => { try { return $"{p.GetClassName()}:'{p.GetName()}'"; } catch (Exception e) { return "!" + e.GetType().Name; } }));
                    }
                    catch (Exception e) { names = "!" + e.GetType().Name; }
                    sb.AppendLine($"    {list.GetType().Name} '{list.Name}' visible={list.IsVisible} {list.ActualWidth:0}x{list.ActualHeight:0} " +
                                  $"items={list.Items.Count} containers={containers} status={list.ItemContainerGenerator.Status} " +
                                  $"virt={VirtualizingPanel.GetIsVirtualizing(list)} peers=[{names}]");
                }
            }
        }
        catch (Exception e) { sb.AppendLine($"  probe threw {e.GetType().Name}: {e.Message}"); }
        finally { window.Close(); }
        return sb.ToString();
    }

    private const System.Windows.Threading.DispatcherPriority DispatcherPriorityIdle = System.Windows.Threading.DispatcherPriority.ApplicationIdle;

    private static void Idle(System.Windows.Threading.DispatcherPriority p) =>
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, p);

    /// <summary>The visible page's lists (not the tab strip, not combo boxes' closed popups).</summary>
    private static IEnumerable<ItemsControl> Lists(DependencyObject root)
    {
        int n = root is Visual ? VisualTreeHelper.GetChildrenCount(root) : 0;
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ItemsControl list && child is not TabControl && child is not ComboBox && child is not MenuBase && list.IsVisible)
                yield return list;
            foreach (var inner in Lists(child)) yield return inner;
        }
    }
}
