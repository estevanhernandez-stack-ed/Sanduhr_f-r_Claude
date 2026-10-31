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
        Model = model;
        DataContext = model;
        Title = SetupCopy.Title;
        model.Applied += _ => Close();
        model.Dismissed += Close;
    }
}
