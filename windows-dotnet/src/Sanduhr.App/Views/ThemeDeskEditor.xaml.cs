using System.Windows.Controls;
using System.Windows.Input;

namespace Sanduhr.App.Views;

/// <summary>
/// The theme's Desk look editor (theme match): Desk Look while following, and the Theme Studio. A row commits when it is left or on
/// Enter; the font commits when its list closes or it loses focus, never on the arrow keys that step through it.
/// </summary>
public partial class ThemeDeskEditor : UserControl
{
    public ThemeDeskEditor()
    {
        InitializeComponent();
        FontBox.DropDownClosed += (_, _) => CommitFont();
        FontBox.LostFocus += (_, _) => CommitFont();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && e.OriginalSource is TextBox box)
                box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        };
    }

    private void CommitFont()
    {
        if (!FontBox.IsDropDownOpen)
            FontBox.GetBindingExpression(ComboBox.SelectedItemProperty)?.UpdateSource();
    }
}
