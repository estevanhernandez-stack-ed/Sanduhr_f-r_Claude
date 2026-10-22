using System.Windows.Controls;

namespace Sanduhr.App.Views;

/// <summary>Settings, Desk, Where each piece sits (spec 5.1, and the Monitor dropdown of Desk pieces per monitor). Its DataContext is the
/// <see cref="ViewModels.DeskSettingsViewModel"/>.</summary>
public partial class DeskPiecesPanel : UserControl
{
    public DeskPiecesPanel() => InitializeComponent();
}
