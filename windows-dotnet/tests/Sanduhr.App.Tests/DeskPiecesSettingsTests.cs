using System.IO;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings v2, slice 3: Desk, Pieces' rows (Show, Order, Settings…) and the feature pages' status lines, over a scratch AppData.
/// Every change goes to desk_layout through DeskArrangement; nothing else is written.
/// </summary>
public sealed class DeskPiecesSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-pieces-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Paths _paths;
    private readonly SettingsStore _store;

    public DeskPiecesSettingsTests()
    {
        Directory.CreateDirectory(_root);
        _paths = new Paths(appDataBase: _root, localAppDataBase: Path.Combine(_root, "local"));
        _store = new SettingsStore(_paths);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private DeskSettingsViewModel Desk(string? layout = null, bool enabled = true)
    {
        _store.SaveDesk((DeskSettings.Default with { Enabled = enabled, Layout = layout ?? DeskArrangement.Standard }).Normalized());
        return new DeskSettingsViewModel(_store, [], () => []);
    }

    private string Layout => _store.LoadDesk().Layout;

    private static DeskPieceRow Row(DeskSettingsViewModel desk, string widget) => desk.Pieces.Single(p => p.Widget == widget);

    [Fact]
    public void Show_places_a_piece_at_its_default_and_off_hides_it()
    {
        var desk = Desk();
        var weather = Row(desk, "weather");
        Assert.False(weather.Shown);
        weather.Shown = true;
        Assert.Equal("clock:bl claude:bl weather:tr", Layout);
        Assert.True(weather.Shown);
        Assert.Equal(DeskAnchor.TR, weather.Place.Anchor);
        weather.Shown = false;
        Assert.Equal(DeskArrangement.Standard, Layout);
        Assert.False(weather.Shown);
    }

    [Fact]
    public void Show_again_in_the_same_window_returns_to_the_last_place()
    {
        var desk = Desk("clock:bl claude:bl weather:bl");
        var weather = Row(desk, "weather");
        weather.Shown = false;
        weather.Shown = true;
        Assert.Equal(DeskAnchor.BL, new DeskArrangement(Layout).Placement("weather")!.Anchor);

        weather.Place = DeskSettingsViewModel.Hidden;       // hidden through Place: remembered too
        Assert.Equal(DeskAnchor.BL, desk.PlaceIfHidden("weather"));

        weather.Shown = false;
        Assert.Equal(DeskAnchor.TR, Desk(Layout).PlaceIfHidden("weather"));   // a new window: the default place again
    }

    [Fact]
    public void Show_returns_to_the_last_visible_place_whatever_hid_the_piece()
    {
        var desk = Desk("clock:bl claude:bl weather:bl");
        var weather = Row(desk, "weather");
        weather.Shown = false;
        weather.Shown = true;                                  // back at BL
        weather.Place = DeskSettingsViewModel.PlaceFor(DeskAnchor.TR);
        _store.SaveDesk(_store.LoadDesk() with { Layout = "clock:bl claude:bl" });   // hidden outside the row (Arrange, a design)
        desk.Reload();
        Assert.False(weather.Shown);
        Assert.Equal(DeskAnchor.TR, desk.PlaceIfHidden("weather"));
    }

    [Fact]
    public void Showing_a_piece_into_an_occupied_place_updates_the_other_rows_Order()
    {
        var desk = Desk("claude:bl weather:tr");
        var claude = Row(desk, "claude");
        Assert.False(claude.CanMoveUp);
        var raised = 0;
        claude.MoveUpCommand.CanExecuteChanged += (_, _) => raised++;
        Row(desk, "clock").Shown = true;                        // joins bottom left, seated above the Claude line
        Assert.Equal("clock:bl claude:bl weather:tr", Layout);
        Assert.True(claude.CanMoveUp);
        Assert.True(raised > 0);
        Assert.True(claude.MoveUpCommand.CanExecute(null));
        Assert.Equal(SettingsNames.MoveUpIn(claude.Name, claude.Place.Name), claude.MoveUpName);

        claude.MoveUpCommand.Execute(null);
        Assert.Equal("claude:bl clock:bl weather:tr", Layout);
        Assert.False(claude.CanMoveUp);
    }

    [Fact]
    public void The_status_properties_are_the_pieces_words()
    {
        var desk = Desk();
        Assert.Same(desk.PieceStatus("watchers"), desk.WatchersPiece);
        Assert.Same(desk.PieceStatus("launcher"), desk.LauncherPiece);
        Assert.Same(desk.PieceStatus("glow"), desk.GlowPiece);
    }

    [Fact]
    public void Order_moves_a_piece_within_its_place_and_is_live_only_where_it_can_move()
    {
        var desk = Desk("clock:bl claude:bl weather:tr");
        var clock = Row(desk, "clock");
        var claude = Row(desk, "claude");
        Assert.False(clock.CanMoveUp);
        Assert.True(clock.CanMoveDown);
        Assert.True(claude.CanMoveUp);
        Assert.False(claude.CanMoveDown);
        Assert.False(Row(desk, "weather").CanMoveUp || Row(desk, "weather").CanMoveDown);   // alone in its place
        Assert.False(Row(desk, "message").CanMoveUp || Row(desk, "message").CanMoveDown);   // hidden
        Assert.Equal("Move Clock and date down in bottom left", clock.MoveDownName);

        clock.MoveDownCommand.Execute(null);
        Assert.Equal("claude:bl clock:bl weather:tr", Layout);
        Assert.True(Row(desk, "clock").CanMoveUp);
        Assert.False(Row(desk, "clock").MoveDownCommand.CanExecute(null));
    }

    [Fact]
    public void A_piece_with_a_page_links_to_it()
    {
        var desk = Desk();
        var weather = Row(desk, "weather");
        Assert.True(weather.HasSettingsPage);
        Assert.Equal("Weather settings…", weather.SettingsTitle);
        Assert.Equal("weather", weather.SettingsLink);
        Assert.Equal("claudecode#glow-where", Row(desk, "glow").SettingsLink);
        Assert.Equal("launcher", Row(desk, "launcher").SettingsLink);
        Assert.False(Row(desk, "clock").HasSettingsPage);
        Assert.Equal("", Row(desk, "clock").SettingsTitle);
        Assert.Equal("Weather, on the Desk", weather.ShownName);
    }

    [Fact]
    public void A_feature_pages_line_says_where_and_places_a_hidden_piece()
    {
        var desk = Desk();
        var status = desk.PieceStatus("weather");
        Assert.Same(status, desk.PieceStatus("weather"));
        Assert.False(status.IsPlaced);
        Assert.Equal("Not on the Desk.", status.Text);
        Assert.True(status.PlaceCommand.CanExecute(null));

        status.PlaceCommand.Execute(null);
        Assert.Equal("clock:bl claude:bl weather:tr", Layout);
        Assert.True(status.IsPlaced);
        Assert.Equal("On the Desk: top right.", status.Text);
        Assert.False(status.PlaceCommand.CanExecute(null));
        Assert.Equal("desk#pieces", status.DeskSettingsLink);
        Assert.Equal("Desk settings…", status.DeskSettingsTitle);
    }

    [Fact]
    public void The_line_says_when_the_Desk_itself_is_off()
    {
        var desk = Desk("clock:bl weather:br", enabled: false);
        Assert.Equal("On the Desk: bottom right. The Desk is off: turn it on in Settings, Desk.", desk.PieceStatus("weather").Text);
        desk.Enabled = true;
        Assert.Equal("On the Desk: bottom right.", desk.PieceStatus("weather").Text);
    }

    [Fact]
    public void The_status_line_follows_a_reload()
    {
        var desk = Desk();
        var status = desk.PieceStatus("weather");
        _store.SaveDesk(_store.LoadDesk() with { Layout = "clock:bl weather:bl" });   // Arrange's Done, a design, another window
        desk.Reload();
        Assert.Equal("On the Desk: bottom left.", status.Text);
    }

    [Fact]
    public void PlaceIfHidden_does_nothing_to_a_placed_piece()
    {
        var desk = Desk("clock:bl weather:bl:1.2");
        var before = File.ReadAllText(_paths.SettingsFile);
        var stamp = File.GetLastWriteTimeUtc(_paths.SettingsFile);
        Assert.Null(desk.PlaceIfHidden("weather"));
        Assert.Null(desk.PlaceIfHidden("nope"));
        Assert.Equal(before, File.ReadAllText(_paths.SettingsFile));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(_paths.SettingsFile));
    }

    [Fact]
    public void Nothing_places_while_Arrange_runs()
    {
        var desk = Desk();
        var status = desk.PieceStatus("weather");
        desk.Arranging = true;
        Assert.Null(desk.PlaceIfHidden("weather"));
        Assert.False(status.CanPlace);
        Assert.False(status.PlaceCommand.CanExecute(null));
        Assert.Equal(DeskArrangement.Standard, Layout);
        desk.Arranging = false;
        Assert.True(status.PlaceCommand.CanExecute(null));
    }

    [Fact]
    public void Hiding_a_placed_piece_raises_CanPlace_once()
    {
        // Item 10a (S3 T7): one change, one notification.
        var desk = Desk("clock:bl claude:bl weather:bl");
        var status = desk.PieceStatus("weather");
        Assert.True(status.IsPlaced);
        int raised = 0;
        status.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(DeskPieceStatusViewModel.CanPlace)) raised++; };
        Row(desk, "weather").Shown = false;
        Assert.False(status.IsPlaced);
        Assert.Equal(1, raised);
    }

    private static System.Windows.Controls.WrapPanel? FirstRow(System.Windows.DependencyObject root)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is System.Windows.Controls.WrapPanel { Tag: "PieceRow" } row) return row;
            if (FirstRow(child) is { } found) return found;
        }
        return null;
    }

    [Fact]
    public void A_row_is_one_line_at_760_of_page_width_and_wraps_below() => Sta.Run(() =>
    {
        var panel = new Sanduhr.App.Views.DeskPiecesPanel { DataContext = Desk("clock:bl claude:bl") };
        double OrderTop(double pageWidth)
        {
            double inner = pageWidth - 40;   // the page's 20 DIP margins
            panel.Measure(new System.Windows.Size(inner, double.PositiveInfinity));
            panel.Arrange(new System.Windows.Rect(0, 0, inner, panel.DesiredSize.Height));
            panel.UpdateLayout();
            var row = FirstRow(panel) ?? throw new Xunit.Sdk.XunitException("no piece row");
            return ((System.Windows.FrameworkElement)row.Children[1]).TranslatePoint(new System.Windows.Point(0, 0), row).Y;
        }
        Assert.Equal(0, OrderTop(760), 1);
        Assert.True(OrderTop(759) > 20, "the Order buttons stay on the first line below 760 DIP");
    });

    [Fact]
    public void Either_watchers_switch_shows_a_hidden_Watchers_piece()
    {
        var desk = Desk();
        desk.WatchersAgents = true;
        Assert.Contains("watchers:mr", Layout);
        desk.Pieces.Single(p => p.Widget == "watchers").Shown = false;
        desk.WatchersAgents = false;
        Assert.DoesNotContain("watchers", Layout);       // turning a switch off never hides the piece, and never shows it
        desk.WatchersCiRunners = true;
        Assert.Contains("watchers:mr", Layout);           // back where it was
        Assert.Equal("On the Desk: middle right.", desk.WatchersPiece.Text);
    }
}
