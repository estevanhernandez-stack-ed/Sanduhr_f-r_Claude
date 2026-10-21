using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>
/// The bar in the middle of the primary screen while arranging (spec 7.3): <see cref="DeskArrangeCopy"/>'s
/// title, hint and keys line over <b>Done</b> (the default, Return) and <b>Cancel</b> (Escape). At least
/// <see cref="DeskArrange.BarMinimumWidth"/> by <see cref="DeskArrange.BarMinimumHeight"/>. Every part
/// carries an accessible name.
/// </summary>
public sealed partial class DeskArrangeBar : UserControl
{
    /// <summary>Done was pressed.</summary>
    public event Action? DoneRequested;

    /// <summary>Cancel was pressed.</summary>
    public event Action? CancelRequested;

    /// <summary>Shows or clears the line that says Done waits (every piece is hidden).</summary>
    public void ShowNothingPlaced(bool on)
    {
        Notice.Text = on ? DeskArrangeCopy.NothingPlaced : "";
        Notice.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(Notice, Notice.Text);
    }

    public DeskArrangeBar()
    {
        InitializeComponent();
        MinWidth = DeskArrange.BarMinimumWidth;
        MinHeight = DeskArrange.BarMinimumHeight;
        MaxWidth = 460;
        Title.Text = DeskArrangeCopy.BarTitle;
        Hint.Text = DeskArrangeCopy.BarHint;
        Keys.Text = DeskArrangeCopy.BarKeys;
        AutomationProperties.SetName(this, DeskArrangeCopy.BarTitle);
        AutomationProperties.SetName(Frame, DeskArrangeCopy.BarTitle);
        AutomationProperties.SetName(Title, DeskArrangeCopy.BarTitle);
        AutomationProperties.SetName(Hint, DeskArrangeCopy.BarHint);
        AutomationProperties.SetName(Keys, DeskArrangeCopy.BarKeys);
        AutomationProperties.SetName(DoneButton, "Done");
        AutomationProperties.SetName(CancelButton, "Cancel");
        DoneButton.Click += (_, _) => DoneRequested?.Invoke();
        CancelButton.Click += (_, _) => CancelRequested?.Invoke();
    }
}
