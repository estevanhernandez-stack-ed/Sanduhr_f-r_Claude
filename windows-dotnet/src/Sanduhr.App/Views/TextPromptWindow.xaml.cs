using System.Windows;

namespace Sanduhr.App.Views;

/// <summary>
/// Minimal single-line text prompt (WPF has no built-in input box). Used by the
/// Accounts tab to collect a new account name on rename. Returns
/// <see cref="Value"/> with <c>DialogResult == true</c> on Save.
/// </summary>
internal partial class TextPromptWindow : Window
{
    /// <summary>The entered text once the user clicks Save; empty otherwise.</summary>
    public string Value { get; private set; } = "";

    public TextPromptWindow(string title, string prompt, string initial)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        PromptText.Text = prompt;
        ValueBox.Text = initial;
        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        Value = ValueBox.Text ?? "";
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
