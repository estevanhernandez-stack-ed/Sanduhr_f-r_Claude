using System.Windows;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Views;

/// <summary>Design it with an AI chat (setup spec, section 3): copy a prompt, paste the answer back, preview the Desk part, use the drawer
/// rules. It stays open while the review bar is up, so a design can be tried, dismissed and tried again from the same paste.</summary>
internal partial class ChatDesignWindow : Window
{
    internal ChatDesignViewModel Model { get; }

    internal ChatDesignWindow(ChatDesignViewModel model)
    {
        InitializeComponent();
        Model = model;
        DataContext = model;
        Title = SetupCopy.DesignWithChat;
        model.CloseRequested = Close;
    }
}
