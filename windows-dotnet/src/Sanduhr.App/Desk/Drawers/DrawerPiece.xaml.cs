using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Sanduhr.App.Services;

namespace Sanduhr.App.Desk;

/// <summary>
/// A drawer on the Desk (5.0 drawers spec, section 2): a themed frame with the drawer's name, and open, a grid of Shell icons and names
/// that scrolls inside it; folded, the title and a count; for a folder that is gone, one line. Its state lives in
/// <see cref="DrawerService"/>, so a redraw builds a new piece over the same <see cref="DrawerState"/>, selection included.
/// </summary>
internal sealed partial class DrawerPiece : UserControl, IDisposable
{
    public const double FrameWidth = 344;
    public const double ListHeight = 264;
    private readonly DrawerState _state;
    private readonly DrawerService _service;

    public DrawerPiece(DrawerState state, DrawerService service, DrawerLook look, double scale)
    {
        InitializeComponent();
        _state = state;
        _service = service;
        Tag = state.Word;
        FontFamily = look.Font;
        Resources["Drawer.Plate"] = Brush(look.Plate);
        Resources["Drawer.Ink"] = Brush(look.Ink);
        Resources["Drawer.Accent"] = Brush(look.Accent);
        Resources["Drawer.Hover"] = Brush(Color.FromArgb(46, look.Accent.R, look.Accent.G, look.Accent.B));
        Resources["Drawer.Selected"] = Brush(Color.FromArgb(90, look.Accent.R, look.Accent.G, look.Accent.B));
        Resources["Drawer.LabelEffect"] = StreamerMode.On ? Blur() : look.Shadow;   // streamer: masked - file labels blur while streamer mode is on
        Frame.Width = FrameWidth;
        List.Height = ListHeight;
        if (Math.Abs(scale - 1) > 0.001) LayoutTransform = new ScaleTransform(scale, scale);
        List.ItemsSource = state.Items;
        foreach (var path in state.Selected)
            if (state.Items.FirstOrDefault(i => i.Path == path) is { } item) List.SelectedItems.Add(item);
        List.SelectionChanged += (_, _) => _state.Selected = [.. List.SelectedItems.OfType<DrawerItem>().Select(i => i.Path)];
        _state.PropertyChanged += OnStateChanged;
        _state.Items.CollectionChanged += OnItemsChanged;
        // The group name is bound to SpokenName, which carries the codename while on (DrawerState.Title masks it), so it follows the count.
        BindingOperations.SetBinding(this, AutomationProperties.NameProperty, new Binding(nameof(DrawerState.SpokenName)) { Source = _state });
        Show();
        Wire();
    }

    public DrawerState State => _state;

    private void Show()
    {
        TitleText.Text = _state.Folded ? _state.FoldedTitle : _state.Title;
        AutomationProperties.SetName(TitleText, TitleText.Text);   // Title and FoldedTitle carry the codename while streamer mode is on
        MissingText.Text = DrawerState.MissingLine;
        bool open = !_state.Folded;
        MissingText.Visibility = open && _state.Missing ? Visibility.Visible : Visibility.Collapsed;
        List.Visibility = open && !_state.Missing ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e) => Show();

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Show();

    protected override AutomationPeer OnCreateAutomationPeer() => new GroupPeer(this);

    /// <summary>A drawer is a named group (spec section 2, the 10.1.4.4 rule): "Screenshots drawer, 14 items".</summary>
    private sealed class GroupPeer(DrawerPiece owner) : UserControlAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
    }

    private static SolidColorBrush Brush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static BlurEffect Blur()
    {
        var b = new BlurEffect { Radius = 6 };
        b.Freeze();
        return b;
    }

    /// <summary>The interaction (Task 8).</summary>
    private void Wire() { }

    private void Editor_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) { }
    private void Editor_KeyDown(object sender, KeyEventArgs e) { }
    private void Editor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) { }

    public void Dispose()
    {
        BindingOperations.ClearBinding(this, AutomationProperties.NameProperty);
        _state.PropertyChanged -= OnStateChanged;
        _state.Items.CollectionChanged -= OnItemsChanged;
    }
}
