using System.Windows.Controls;

namespace Sanduhr.App.Views;

/// <summary>Settings, Desk, Pieces (spec, Desk: the split): each piece's Show, place, size, monitor, style or page link, and order. Its DataContext is the
/// <see cref="ViewModels.DeskSettingsViewModel"/>.</summary>
public partial class DeskPiecesPanel : UserControl
{
    public DeskPiecesPanel() => InitializeComponent();
}
