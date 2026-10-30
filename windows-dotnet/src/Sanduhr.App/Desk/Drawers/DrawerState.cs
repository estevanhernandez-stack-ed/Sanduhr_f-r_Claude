using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>One file or folder in a drawer. <see cref="Pending"/> marks one a plan under review would move here (drawn faded).</summary>
internal sealed partial class DrawerItem : ObservableObject
{
    public DrawerItem(string path, bool isFolder, bool pending)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(path);   // streamer: key - the item's own name, for lookups; the drawn label blurs in the piece and SpokenName masks the spoken one
        IsFolder = isFolder;
        Pending = pending;
    }

    public string Path { get; }
    public string Name { get; }
    public bool IsFolder { get; }
    public bool Pending { get; }

    [ObservableProperty] private ImageSource? _icon;
    [ObservableProperty] private bool _editing;

    /// <summary>The item's accessible name: its file name (spec section 2), or "File" while streamer mode is on.</summary>
    public string SpokenName => StreamerMode.Mask.Omit(Name) ?? (IsFolder ? "Folder" : "File");
}

/// <summary>One drawer's live state, kept across Desk redraws (the piece is rebuilt, this is not).</summary>
internal sealed partial class DrawerState : ObservableObject
{
    public const string MissingLine = "This drawer's folder is gone.";

    public DrawerState(DrawerDef def) => Def = def;

    public DrawerDef Def { get; internal set; }
    public ObservableCollection<DrawerItem> Items { get; } = [];
    public string Word => DrawerPieces.Word(Def.Id);

    [ObservableProperty] private bool _folded;
    [ObservableProperty] private bool _missing;

    /// <summary>The drawer's name as drawn: its codename while streamer mode is on.</summary>
    public string Title => StreamerMode.Mask.Name(CodenameCategory.Folder, Def.Name);   // streamer: masked - Mask.Name gives the codename while on

    private int Count => Items.Count(i => !i.Pending);

    /// <summary>The folded title bar: "Screenshots · 14".</summary>
    public string FoldedTitle => $"{Title} · {Count}";

    /// <summary>The drawer's group name (spec section 2): "Screenshots drawer, 14 items".</summary>
    public string SpokenName => $"{Title} drawer, {Count} {(Count == 1 ? "item" : "items")}";

    /// <summary>The item list changed: the titles that count follow.</summary>
    internal void CountChanged()
    {
        OnPropertyChanged(nameof(FoldedTitle));
        OnPropertyChanged(nameof(SpokenName));
    }
}
