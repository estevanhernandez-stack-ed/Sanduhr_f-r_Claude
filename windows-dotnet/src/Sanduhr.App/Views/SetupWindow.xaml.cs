using System.Windows;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Views;

/// <summary>Set up Sanduhr. Closing it without Continue is Not now (first run) or Cancel (later): nothing is saved, so a first run asks
/// again on the next launch (setup spec, section 2).</summary>
internal partial class SetupWindow : Window
{
    internal SetupViewModel Model { get; }

    internal SetupWindow(SetupViewModel model)
    {
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height;   // never taller than the screen: the tiles scroll, Continue stays
        Model = model;
        DataContext = model;
        Title = SetupCopy.Title;
        model.Applied += _ => Close();
        model.Dismissed += Close;
    }

    /// <summary>The wheel over the looks strip scrolls it sideways instead of going nowhere (the strip has no vertical scroll of its own).</summary>
    private void LooksStrip_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        LooksStrip.ScrollToHorizontalOffset(LooksStrip.HorizontalOffset - e.Delta);
        e.Handled = true;
    }
}
