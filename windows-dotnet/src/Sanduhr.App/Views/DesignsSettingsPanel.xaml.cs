using System.Windows.Controls;

namespace Sanduhr.App.Views;

/// <summary>Settings, Desk, Designs (spec, the agent-designed Desk, saved designs). Its DataContext is a
/// <see cref="ViewModels.DesignsSettingsViewModel"/>.</summary>
public partial class DesignsSettingsPanel : UserControl
{
    public DesignsSettingsPanel() => InitializeComponent();
}
