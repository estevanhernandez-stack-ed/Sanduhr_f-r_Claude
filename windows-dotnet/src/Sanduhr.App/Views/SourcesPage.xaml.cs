using System.Windows.Controls;
using System.Windows.Input;

namespace Sanduhr.App.Views;

/// <summary>Settings, Usage, Sources (agent layer). Enter in a color box saves it, as leaving the box does.</summary>
public partial class SourcesPage : UserControl
{
    public SourcesPage() => InitializeComponent();

    private void OnColorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox box) return;
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        e.Handled = true;
    }
}
