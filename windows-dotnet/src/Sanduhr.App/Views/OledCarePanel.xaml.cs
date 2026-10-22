using System.Windows.Controls;

namespace Sanduhr.App.Views;

/// <summary>Settings, Desk, OLED care (spec 3.7.0). Its DataContext is the <see cref="ViewModels.DeskSettingsViewModel"/>.</summary>
public partial class OledCarePanel : UserControl
{
    public OledCarePanel() => InitializeComponent();
}
