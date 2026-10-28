using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Services;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>One row of Show the real name: a mapped or shown name, as the list may show it now (spec, the allowlist C).</summary>
public sealed partial class CodenameRow : ObservableObject
{
    private readonly StreamerService _service;
    private bool _loading;

    public CodenameCategory Category { get; }
    /// <summary>The book's key: a label, a folder's lower-cased full path, a project's name.</summary>
    public string Real { get; }
    public string CategoryTitle => SettingsNames.CodenameCategoryTitle(Category);

    [ObservableProperty] private string _codename = "";
    /// <summary>On: the codename alone. Off: "codename · real name", or the real name alone when it never had a codename.</summary>
    [ObservableProperty] private string _text = "";
    /// <summary>The checkbox's spoken name: "Show the real name on stream, Dune Fox".</summary>
    [ObservableProperty] private string _name = "";
    /// <summary>The first row of its category: its heading shows above it.</summary>
    [ObservableProperty] private bool _startsCategory;
    [ObservableProperty] private bool _shown;

    internal CodenameRow(StreamerService service, CodenameEntry entry)
    {
        _service = service;
        Category = entry.Category;
        Real = entry.Real;
    }

    internal void Update(CodenameEntry entry, bool on, string realShown, bool startsCategory)
    {
        _loading = true;
        Codename = entry.Codename ?? "";
        Text = on ? Codename : entry.Codename is { } code ? SettingsNames.AllowlistRowOff(code, realShown) : realShown;
        Name = SettingsNames.AllowlistRowName(on ? Codename : Text);
        StartsCategory = startsCategory;
        Shown = entry.Shown;
        _loading = false;
    }

    partial void OnShownChanged(bool value)
    {
        if (!_loading)
            _service.SetShown(Category, Real, value);
    }

    public override string ToString() => Name;
}

/// <summary>
/// Settings, Privacy, Streamer mode and Show the real name (Settings v2, slice 7; spec, Streamer mode). The switch writes the widget's
/// service, as the tray's and the widget's items do. The window's one subscription to something that outlives it: <see cref="Detach"/>
/// on Closed (decision 15). <see cref="NamesChanged"/> tells Settings to build every masked page's text again.
/// </summary>
public sealed partial class StreamerSettingsViewModel : ObservableObject
{
    private readonly StreamerService _service;
    private readonly Func<string, string> _showFolder;
    private Window? _owner;
    private bool _attached;
    private bool _namesQueued;

    /// <param name="showFolder">How a folder key shows while off (<c>~\.claude-work</c>); <see cref="ClaudeCodeFolders.Display"/> under the
    /// Claude Code pages' profile folder when null.</param>
    public StreamerSettingsViewModel(StreamerService service, Func<string, string>? showFolder = null)
    {
        _service = service;
        _showFolder = showFolder ?? (key => ClaudeCodeFolders.Display(key, ClaudeCodeProfile.Dir));   // streamer: key - default viewer of a folder key while the mode is off; the row shows the codename while on
        Read();
    }

    public bool On
    {
        get => _service.On;
        set
        {
            if (value != _service.On)
                _service.SetOn(value);
        }
    }

    public ObservableCollection<CodenameRow> Rows { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoRows))]
    private bool _hasRows;

    public bool NoRows => !HasRows;

    public string AllowlistCaption => On ? SettingsNames.AllowlistCaptionOn : SettingsNames.AllowlistCaptionOff;

    /// <summary>A confirmed Shuffle, said once through a live region (preflight P14; the line is <see cref="SettingsNames.ShuffleDone"/>). The flag goes false first so every Shuffle raises again.</summary>
    [ObservableProperty] private bool _shuffleShown;

    /// <summary>Set by Settings: every masked page builds its text again.</summary>
    public Action? NamesChanged { get; set; }

    /// <summary>Tests answer Shuffle's question here.</summary>
    internal Func<bool>? ConfirmShuffleHook { get; set; }

    /// <summary>The window is built: listen to the service (preflight P15: not in the constructor, so a window that fails to open leaves
    /// nothing subscribed). <see cref="Detach"/> on Closed.</summary>
    public void AttachOwner(Window owner)
    {
        _owner = owner;
        if (_attached)
            return;
        _attached = true;
        _service.Changed += OnChanged;
    }

    [RelayCommand]
    private void Shuffle()
    {
        bool yes = ConfirmShuffleHook is { } hook
            ? hook()
            : ThemedDialog.ShowConfirm(_owner, SettingsNames.ShuffleQuestion, SettingsNames.ShuffleDetail,
                SettingsNames.ShuffleConfirm, SettingsNames.ShuffleCancel);
        if (!yes)
            return;
        _service.Shuffle();
        ShuffleShown = false;
        ShuffleShown = true;
    }

    /// <summary>Reads the book again. Rows are updated in place by (category, name), so a ticked row keeps its focus.</summary>
    public void Read()
    {
        bool on = _service.On;
        var entries = _service.Entries();
        var keep = new HashSet<(CodenameCategory, string)>(entries.Select(e => (e.Category, e.Real)));
        for (int i = Rows.Count - 1; i >= 0; i--)
            if (!keep.Contains((Rows[i].Category, Rows[i].Real)))
                Rows.RemoveAt(i);
        CodenameCategory? last = null;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            int at = IndexOf(e);
            CodenameRow row;
            if (at < 0)
            {
                row = new CodenameRow(_service, e);
                Rows.Insert(Math.Min(i, Rows.Count), row);
            }
            else
            {
                row = Rows[at];
                if (at != i && i < Rows.Count)
                    Rows.Move(at, i);
            }
            row.Update(e, on, e.Category == CodenameCategory.Folder ? _showFolder(e.Real) : e.Real, last != e.Category);
            last = e.Category;
        }
        HasRows = Rows.Count > 0;
    }

    public void Detach()
    {
        _attached = false;
        _service.Changed -= OnChanged;
    }

    private int IndexOf(CodenameEntry e)
    {
        for (int i = 0; i < Rows.Count; i++)
            if (Rows[i].Category == e.Category && Rows[i].Real == e.Real)
                return i;
        return -1;
    }

    private void OnChanged()
    {
        OnPropertyChanged(nameof(On));
        OnPropertyChanged(nameof(AllowlistCaption));
        Read();
        // Preflight P16: ticking ten rows reloads the pages once per turn, not ten times.
        if (_namesQueued)
            return;
        _namesQueued = true;
        (_owner?.Dispatcher ?? Dispatcher.CurrentDispatcher).BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _namesQueued = false;
            if (_attached)
                NamesChanged?.Invoke();
        }));
    }
}
