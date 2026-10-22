using System.Windows.Controls;

namespace Sanduhr.App.Views;

/// <summary>Settings, Now playing, Looks (spec, a look per song). Its DataContext is the <see cref="ViewModels.NowPlayingPageViewModel"/>.</summary>
public partial class NowPlayingLooksPanel : UserControl
{
    public NowPlayingLooksPanel() => InitializeComponent();
}
