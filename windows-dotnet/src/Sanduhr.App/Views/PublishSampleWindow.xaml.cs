using System.Windows;

namespace Sanduhr.App.Views;

/// <summary>Publish usage's "Show a sample…" (WF28): the next record, read-only and selectable, with where it would go.
/// Close is the default and the cancel button: closing a read-only preview destroys nothing, and Escape closes it.</summary>
internal partial class PublishSampleWindow : Window
{
    public PublishSampleWindow(string header, string note, string json)
    {
        InitializeComponent();
        HeaderText.Text = header;
        NoteText.Text = note;
        NoteText.Visibility = note.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        RecordBox.Text = json;
        Loaded += (_, _) => RecordBox.Focus();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
