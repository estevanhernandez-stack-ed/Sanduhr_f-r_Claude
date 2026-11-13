using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Sanduhr.App.Views;

/// <summary>Settings, Usage, Sources (agent layer). Enter in a color box saves it, as leaving the box does; the arrow keys move
/// the Primary choice between the rows' radios.</summary>
public partial class SourcesPage : UserControl
{
    public SourcesPage() => InitializeComponent();

    private void OnColorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox box) return;
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        e.Handled = true;
    }

    /// <summary>
    /// Each row's Primary radio sits in its own item, and WPF moves between radios with the arrows only inside one parent, so
    /// Down from Claude's went to Codex's Track box (4.8.0 hand pass). The arrows step to the next enabled radio of the group,
    /// wrapping at the ends, and check it, as a Windows radio group does.
    /// </summary>
    private void OnPrimaryKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not RadioButton from) return;
        var radios = new List<RadioButton>();
        Collect(PageBody, from.GroupName, radios);
        if (Step(radios, from, e.Key) is not { } to) return;
        to.IsChecked = true;
        to.Focus();
        e.Handled = true;
    }

    /// <summary>The radio the arrow <paramref name="key"/> lands on from <paramref name="from"/>: the next enabled one down or
    /// right, the one before up or left, wrapping; null for any other key, or when no other radio is enabled.</summary>
    internal static RadioButton? Step(IReadOnlyList<RadioButton> radios, RadioButton from, Key key)
    {
        int dir = key switch { Key.Down or Key.Right => 1, Key.Up or Key.Left => -1, _ => 0 };
        int at = IndexOf(radios, from);
        if (dir == 0 || at < 0) return null;
        for (int i = 1; i < radios.Count; i++)
        {
            var r = radios[((at + dir * i) % radios.Count + radios.Count) % radios.Count];
            if (r.IsEnabled) return r;
        }
        return null;
    }

    private static int IndexOf(IReadOnlyList<RadioButton> radios, RadioButton r)
    {
        for (int i = 0; i < radios.Count; i++)
            if (ReferenceEquals(radios[i], r)) return i;
        return -1;
    }

    private static void Collect(DependencyObject node, string group, List<RadioButton> into)
    {
        if (node is RadioButton r && r.GroupName == group) into.Add(r);
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            Collect(VisualTreeHelper.GetChild(node, i), group, into);
    }
}
