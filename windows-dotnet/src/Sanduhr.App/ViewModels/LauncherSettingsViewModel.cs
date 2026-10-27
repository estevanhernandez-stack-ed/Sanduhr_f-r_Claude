using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Sanduhr.App.Services;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>One Launcher button in Settings, Desk, Launcher: what it opens, its label and glow, and its picture.</summary>
public sealed partial class LauncherButtonRow : ObservableObject
{
    private readonly LauncherSettingsViewModel _owner;
    private bool _loading;

    public LauncherButtonRow(LauncherSettingsViewModel owner, int index, LauncherButton button, string? appName)
    {
        _owner = owner;
        Index = index;
        Kind = button.Kind;
        Target = button.Target;
        AppName = appName;
        HasImage = button.Image is not null;
        _loading = true;
        Label = button.Label ?? "";
        Glow = button.Glow ?? "";
        _loading = false;
    }

    public int Index { get; }
    public LauncherKind Kind { get; }
    public string Target { get; }

    /// <summary>The Start menu's name for the app, once known.</summary>
    public string? AppName { get; private set; }

    public bool IsApp => Kind == LauncherKind.App;

    /// <summary>The name this button goes by: its label, else the app's name, else the site.</summary>
    public string Title => string.IsNullOrWhiteSpace(Label)
        ? (Kind == LauncherKind.Link ? LauncherTarget.LinkHost(Target) : AppName ?? StreamerMode.Mask.Paths(Target))   // streamer: a raw target can be a path
        : Label.Trim();

    /// <summary>The line under the title: what the button opens.</summary>
    /// <summary>A link's full URL can name a private repo, so while streamer mode is on it shows scheme://host/… with the host as its codename.</summary>
    public string Detail => Kind == LauncherKind.Link ? StreamerMode.Mask.Endpoint(Target) : (AppName is null ? "App" : $"App, {AppName}");

    /// <summary>"Use the app's icon" for an app; a link has no app, so it goes back to the link glyph.</summary>
    public string UseDefaultImageText => IsApp ? "Use the app's icon" : "Use the link icon";

    /// <summary>What a screen reader calls this button when a control belongs to it: the app's name or the site, never the label being typed.</summary>
    public string Subject => Kind == LauncherKind.Link ? LauncherTarget.LinkHost(Target) : AppName ?? Target;

    public string UseDefaultImageName => $"{UseDefaultImageText} for {Subject}";

    public string Initial => Title.Length > 0 ? Title[..1].ToUpperInvariant() : "?";

    /// <summary>What a screen reader reads for the row: the button's name, never the type name.</summary>
    public override string ToString() => Title;

    [ObservableProperty] private bool _hasImage;
    [ObservableProperty] private bool _isMissing;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private ImageSource? _preview;

    /// <summary>Typed into the Label box; saved when the box loses focus (24 characters at most).</summary>
    [ObservableProperty] private string _label = "";

    /// <summary>Typed into the Glow color box (#rrggbb); empty uses the Desk ink.</summary>
    [ObservableProperty] private string _glow = "";

    /// <summary>Why the glow text was not saved, or empty.</summary>
    [ObservableProperty] private string _glowProblem = "";

    public bool HasGlowProblem => GlowProblem.Length > 0;
    public bool CanUseDefaultImage => HasImage;
    public bool CanMoveUp => Index > 0;
    public bool CanMoveDown => Index < _owner.Rows.Count - 1;

    partial void OnHasImageChanged(bool value) => OnPropertyChanged(nameof(CanUseDefaultImage));
    partial void OnGlowProblemChanged(string value) => OnPropertyChanged(nameof(HasGlowProblem));

    partial void OnLabelChanged(string value)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Initial));
        if (!_loading) _owner.SetLabel(this, value);
    }

    partial void OnGlowChanged(string value)
    {
        if (!_loading) _owner.SetGlow(this, value);
    }

    internal void SetName(string? name)
    {
        if (AppName == name) return;
        AppName = name;
        OnPropertyChanged(nameof(AppName));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Detail));
        OnPropertyChanged(nameof(Subject));
        OnPropertyChanged(nameof(UseDefaultImageName));
        OnPropertyChanged(nameof(Initial));
    }

    /// <summary>Puts the saved text back in the box without saving it again.</summary>
    internal void Show(string label, string glow)
    {
        _loading = true;
        Label = label;
        Glow = glow;
        _loading = false;
    }

    internal void Refresh() => OnPropertyChanged(nameof(CanMoveDown));
}

/// <summary>
/// Settings, Desk, Launcher (spec, Desk Launcher): the buttons as a list you can add to, remove from and reorder, each
/// with its label, glow color and picture; row or column; and the way into the app picker and the link prompt. Every
/// change saves at once, removes the picture files nothing uses any more, and tells the running Desk. The piece
/// itself is placed in Settings, Desk, Pieces, like any other.
/// </summary>
public sealed partial class LauncherSettingsViewModel : ObservableObject
{
    public static readonly IReadOnlyList<string> OrientationChoices = ["Row", "Column"];

    private readonly SettingsStore _store;
    private readonly Paths _paths;
    private readonly Func<bool> _isPlaced;
    private readonly Func<string?>? _autoPlace;
    private string _autoNote = "";
    private LauncherAppCache? _apps;
    private LauncherModel _model;
    private Window? _owner;
    private bool _loading;

    public LauncherSettingsViewModel(SettingsStore store, Paths paths, Func<bool> isPlaced, Func<string?>? autoPlace = null)
    {
        _autoPlace = autoPlace;
        _store = store;
        _paths = paths;
        _isPlaced = isPlaced;
        _model = store.LoadLauncher();
        Rebuild();
    }

    /// <summary>The cache of the Start menu the app shares (the Desk uses the same one); a private one when the app has none (tests).</summary>
    private LauncherAppCache Apps => _apps ??= LauncherAppCache.Current ?? (_ownCache = new LauncherAppCache());
    private LauncherAppCache? _ownCache;

    /// <summary>Called after every saved change (the Desk re-reads its settings).</summary>
    public Action? Changed { get; set; }

    public ObservableCollection<LauncherButtonRow> Rows { get; } = [];

    public bool HasButtons => Rows.Count > 0;
    public bool HasNoButtons => Rows.Count == 0;
    public bool CanAdd => Rows.Count < LauncherModel.MaxButtons;

    /// <summary>"3 of 8 buttons".</summary>
    public string CountText => $"{Rows.Count} of {LauncherModel.MaxButtons} buttons";

    /// <summary>Where the first button put the piece ("Placed at top left. …"). A hidden Launcher is said by the page's status line
    /// at its top (Principle 1), not here.</summary>
    public string PlacementNote => HasButtons && _isPlaced() ? _autoNote : "";

    public bool HasPlacementNote => PlacementNote.Length > 0;

    /// <summary>Tells the page the piece's place changed (the note under the list follows). A place chosen by hand ends the "Placed at" note.</summary>
    public void PlacementChanged()
    {
        _autoNote = "";
        RaiseNote();
    }

    private void RaiseNote()
    {
        OnPropertyChanged(nameof(PlacementNote));
        OnPropertyChanged(nameof(HasPlacementNote));
    }

    [ObservableProperty] private string _problem = "";

    public bool HasProblem => Problem.Length > 0;

    partial void OnProblemChanged(string value) => OnPropertyChanged(nameof(HasProblem));

    public string SelectedOrientation
    {
        get => _model.Orientation == LauncherOrientation.Column ? "Column" : "Row";
        set
        {
            var next = value == "Column" ? LauncherOrientation.Column : LauncherOrientation.Row;
            if (_loading || next == _model.Orientation) return;
            Commit(_model.WithOrientation(next), rebuild: false);
            OnPropertyChanged();
        }
    }

    /// <summary>Settings is showing: modal dialogs parent to this window, and icons that arrive refresh the list.</summary>
    public void AttachOwner(Window owner)
    {
        _owner = owner;
        Apps.Changed += OnAppsChanged;
        Apps.Refresh([.. _model.Buttons.Where(b => b.Kind == LauncherKind.App).Select(b => b.Target)]);
        // What the cache already knows (the Desk filled it) shows at once; the rest follows through Changed.
        Decorate();
    }

    /// <summary>Settings closed.</summary>
    public void Detach()
    {
        if (_apps is not null) _apps.Changed -= OnAppsChanged;
        _ownCache?.Dispose();
        _ownCache = null;
        _apps = null;
        _owner = null;
    }

    /// <summary>Reads the saved buttons again (Reset, or another writer changed them).</summary>
    public void Reload()
    {
        _model = _store.LoadLauncher();
        Rebuild();
    }

    private void OnAppsChanged() => Decorate();

    private void Rebuild()
    {
        _loading = true;
        var selected = Rows.FirstOrDefault(r => r.IsSelected)?.Index;
        Rows.Clear();
        for (int i = 0; i < _model.Buttons.Count; i++)
            Rows.Add(new LauncherButtonRow(this, i, _model.Buttons[i], _apps?.NameOf(_model.Buttons[i].Target)) { IsSelected = i == selected });
        foreach (var r in Rows) r.Refresh();
        Decorate();
        _loading = false;
        OnPropertyChanged(nameof(HasButtons));
        OnPropertyChanged(nameof(HasNoButtons));
        OnPropertyChanged(nameof(CanAdd));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(SelectedOrientation));
        RaiseNote();
        AddAppCommand.NotifyCanExecuteChanged();
        AddLinkCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Fills in what the Start menu and the images folder know: names, missing apps and pictures.</summary>
    private void Decorate()
    {
        for (int i = 0; i < Rows.Count && i < _model.Buttons.Count; i++)
        {
            var row = Rows[i];
            var button = _model.Buttons[i];
            if (button.Kind == LauncherKind.App && _apps is not null)
            {
                row.SetName(_apps.NameOf(button.Target));
                row.IsMissing = _apps.IsMissing(button.Target);
            }
            ImageSource? preview = button.Image is { } file ? LauncherImages.Load(_paths, file) : null;
            if (preview is null && button.Kind == LauncherKind.App && _apps is not null) preview = _apps.IconOf(button.Target, 64);
            row.Preview = preview;
        }
    }

    /// <summary>Selects a button's row (a click on a missing app opens Settings at it).</summary>
    public void Select(int index)
    {
        foreach (var r in Rows) r.IsSelected = r.Index == index;
    }

    // ---- saving ----

    private void Commit(LauncherModel next, bool rebuild)
    {
        if (ReferenceEquals(next, _model)) return;
        var orphans = LauncherModel.Orphaned(_model, next);
        _store.SaveLauncher(next);
        _model = next;
        // A picture a saved design still uses stays on disk (spec, the agent-designed Desk, saved designs).
        if (orphans.Count > 0)
        {
            var used = new DesignStore(_paths).UsedImages();
            LauncherImages.Delete(_paths, orphans.Where(o => !used.Contains(o)).ToList());
        }
        Problem = "";
        if (rebuild) Rebuild();
        else
        {
            OnPropertyChanged(nameof(CountText));
            RaiseNote();
        }
        Changed?.Invoke();
    }

    internal void SetLabel(LauncherButtonRow row, string text)
    {
        if (row.Index >= _model.Buttons.Count) return;
        var current = _model.Buttons[row.Index];
        var label = LauncherButton.NormalizeLabel(text);
        if (label == current.Label) return;
        Commit(_model.Replace(row.Index, current with { Label = label }), rebuild: false);
    }

    internal void SetGlow(LauncherButtonRow row, string text)
    {
        if (row.Index >= _model.Buttons.Count) return;
        var current = _model.Buttons[row.Index];
        if (string.IsNullOrWhiteSpace(text))
        {
            row.GlowProblem = "";
            if (current.Glow is not null) Commit(_model.Replace(row.Index, current with { Glow = null }), rebuild: false);
            return;
        }
        var glow = LauncherButton.NormalizeGlow(text);
        if (glow is null)
        {
            row.GlowProblem = "Use a color like #ff8800.";
            return;
        }
        row.GlowProblem = "";
        if (glow != current.Glow) Commit(_model.Replace(row.Index, current with { Glow = glow }), rebuild: false);
    }

    // ---- commands ----

    private bool CanAddButton() => CanAdd;

    /// <summary>Add app…: the searchable Start-menu list with icons.</summary>
    [RelayCommand(CanExecute = nameof(CanAddButton))]
    private void AddApp()
    {
        var picker = new LauncherAppPickerWindow(Apps);
        if (_owner is { IsLoaded: true }) picker.Owner = _owner;
        if (picker.ShowDialog() != true || picker.Chosen is not { } app) return;
        Add(LauncherButton.Create(LauncherKind.App, app.Aumid), $"Sanduhr couldn't add {app.Name}.");
    }

    /// <summary>Add link…: an https address and an optional label.</summary>
    [RelayCommand(CanExecute = nameof(CanAddButton))]
    private void AddLink()
    {
        var prompt = new LauncherLinkWindow();
        if (_owner is { IsLoaded: true }) prompt.Owner = _owner;
        if (prompt.ShowDialog() != true) return;
        Add(LauncherButton.Create(LauncherKind.Link, prompt.Url, null, prompt.LinkLabel, null), "That link can't be added.");
    }

    internal void Add(LauncherButton? button, string refusal)
    {
        if (button is null) { Problem = refusal; return; }
        // Membership: an app button is saved only while the live Start-menu listing has the app.
        if (button.Kind == LauncherKind.App && !(Apps.Known && !Apps.IsMissing(button.Target))) { Problem = refusal; return; }
        var next = _model.Add(button);
        if (ReferenceEquals(next, _model)) { Problem = $"The Launcher holds {LauncherModel.MaxButtons} buttons at most."; return; }
        var first = _model.Buttons.Count == 0;
        Commit(next, rebuild: true);
        Select(Rows.Count - 1);
        if (first && !_isPlaced() && _autoPlace?.Invoke() is { } where)
        {
            _autoNote = $"Placed at {where}. Move it in Settings, Desk, Pieces.";
            RaiseNote();
        }
    }

    [RelayCommand]
    private void Remove(LauncherButtonRow? row)
    {
        if (row is null) return;
        Commit(_model.Remove(row.Index), rebuild: true);
    }

    [RelayCommand]
    private void MoveUp(LauncherButtonRow? row)
    {
        if (row is null || row.Index == 0) return;
        Commit(_model.Move(row.Index, -1), rebuild: true);
        Select(row.Index - 1);
    }

    [RelayCommand]
    private void MoveDown(LauncherButtonRow? row)
    {
        if (row is null || row.Index >= _model.Buttons.Count - 1) return;
        Commit(_model.Move(row.Index, 1), rebuild: true);
        Select(row.Index + 1);
    }

    /// <summary>Change image…: a PNG, at most 1 MB, drawn into a 256 px square and kept in Sanduhr's own folder.</summary>
    [RelayCommand]
    private void ChangeImage(LauncherButtonRow? row)
    {
        if (row is null || row.Index >= _model.Buttons.Count) return;
        var dialog = new OpenFileDialog
        {
            Title = $"Choose an image for {row.Title}",
            Filter = "PNG image (*.png)|*.png",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(_owner) != true) return;
        var imported = LauncherImages.Import(_paths, dialog.FileName);
        if (!imported.Ok)
        {
            Problem = imported.Error ?? "Sanduhr couldn't read that image.";
            return;
        }
        var current = _model.Buttons[row.Index];
        Commit(_model.Replace(row.Index, current with { Image = imported.FileName }), rebuild: true);
        Select(row.Index);
    }

    /// <summary>Use the app's icon (or the link icon): drops the chosen image and the file with it.</summary>
    [RelayCommand]
    private void UseDefaultImage(LauncherButtonRow? row)
    {
        if (row is null || row.Index >= _model.Buttons.Count) return;
        var current = _model.Buttons[row.Index];
        if (current.Image is null) return;
        Commit(_model.Replace(row.Index, current with { Image = null }), rebuild: true);
        Select(row.Index);
    }
}
