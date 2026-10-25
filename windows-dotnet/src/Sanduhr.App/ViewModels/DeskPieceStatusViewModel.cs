using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// A feature page's line about its Desk piece (Settings v2, Principle 1): "On the Desk: bottom right." with "Desk settings…", or
/// "Not on the Desk." with "Place it on the Desk", which places it the way Desk, Pieces' Show box would. A status, never a second
/// control: it reads and writes desk_layout through <see cref="DeskSettingsViewModel"/>, so the two can't disagree. It follows
/// every change to the layout (a save, a reload after Arrange or a design), the Desk switch and Arrange mode.
/// </summary>
public sealed partial class DeskPieceStatusViewModel : ObservableObject
{
    private readonly DeskSettingsViewModel _desk;

    internal DeskPieceStatusViewModel(DeskSettingsViewModel desk, string widget)
    {
        _desk = desk;
        Widget = widget;
        desk.PropertyChanged += OnDeskChanged;
        Refresh();
    }

    public string Widget { get; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsHidden), nameof(CanPlace))] private bool _isPlaced;
    [ObservableProperty] private string _text = "";

    public bool IsHidden => !IsPlaced;

    /// <summary>"Place it on the Desk" is live while the piece is hidden and no Arrange session runs (Done would overwrite it).</summary>
    public bool CanPlace => !IsPlaced && !_desk.Arranging;

    public string PlaceTitle => SettingsNames.PlaceOnDesk;
    public string DeskSettingsTitle => SettingsNames.LinkTitle(SettingsPage.Desk);
    public string DeskSettingsLink => SettingsIndex.PageId(SettingsPage.Desk) + "#" + SettingsIndex.DeskPieces;

    /// <summary>A feature's own on-action (Principle 4): places the piece if it is hidden; null when nothing moved.</summary>
    public DeskAnchor? PlaceIfHidden() => _desk.PlaceIfHidden(Widget);

    [RelayCommand(CanExecute = nameof(CanPlace))]
    private void Place() => PlaceIfHidden();

    /// <summary>Reads the piece's place again.</summary>
    public void Refresh()
    {
        var placed = new DeskArrangement(_desk.Layout).Placement(Widget);
        IsPlaced = placed is not null;
        Text = SettingsNames.PieceStatus(placed is null ? null : DeskAnchors.Name(placed.Anchor), _desk.Enabled);
        OnPropertyChanged(nameof(CanPlace));
        PlaceCommand.NotifyCanExecuteChanged();
    }

    private void OnDeskChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DeskSettingsViewModel.Layout) or nameof(DeskSettingsViewModel.Enabled)
            or nameof(DeskSettingsViewModel.Arranging))
            Refresh();
    }
}
