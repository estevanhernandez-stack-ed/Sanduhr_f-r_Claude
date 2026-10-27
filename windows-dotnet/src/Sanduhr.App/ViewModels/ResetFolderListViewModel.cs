using System.ComponentModel;
using System.IO;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>One Claude Code folder in Reset's list (Settings v2, slice 8; R12). The choice keys on <see cref="Path"/>, the real folder;
/// <see cref="Shown"/> is what the row draws, its codename while streamer mode hides it (decision 14).</summary>
internal sealed class ResetFolderRow : INotifyPropertyChanged
{
    private bool _undo = true;
    private string _shown;

    public ResetFolderRow(ResetFolder folder, bool discovered, bool added = false)
    {
        Folder = folder;
        Path = folder.Path;
        Discovered = discovered;
        Added = added;
        HasMeters = folder.Kinds.Contains(IntegrationKind.Statusline) || folder.Kinds.Contains(IntegrationKind.Meters);
        HasMcp = folder.Kinds.Contains(IntegrationKind.Mcp);
        Kinds = SettingsNames.ResetFolderKinds(folder.Kinds);
        _shown = ShownFor(Path);
    }

    /// <summary>The folder and its kinds as the service takes them.</summary>
    public ResetFolder Folder { get; }
    /// <summary>The real folder: what Reset keeps or undoes.</summary>
    public string Path { get; }
    /// <summary>Found by Settings, not added by hand.</summary>
    public bool Discovered { get; }
    /// <summary>Listed in <c>cc_folders_added</c>: it leaves the Claude Code page with the settings.</summary>
    public bool Added { get; }
    /// <summary>It holds a statusline or the meters mod, so it shows usage.</summary>
    public bool HasMeters { get; }
    /// <summary>It holds Sanduhr's MCP server.</summary>
    public bool HasMcp { get; }
    public string Kinds { get; }
    public string Shown => _shown;

    /// <summary>Ticked: Reset takes Sanduhr's entries out of this folder.</summary>
    public bool Undo
    {
        get => _undo;
        set
        {
            if (_undo == value) return;
            _undo = value;
            Raise(nameof(Undo));
            UndoChanged?.Invoke();
        }
    }

    internal event Action? UndoChanged;

    /// <summary>Streamer mode moved: the row draws its folder again (ReloadNames, decision 13).</summary>
    internal void RefreshName()
    {
        _shown = ShownFor(Path);
        Raise(nameof(Shown));
    }

    private static string ShownFor(string path) =>
        StreamerMode.ShowFolder(path, ClaudeCodeProfile.Dir);   // streamer: masked - CP 7: ShowFolder masks on this line

    public override string ToString() => Shown;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Which of the dialog's lines show (decision 6): each one only in the state it is true for. <c>Kept</c> is the row-level line
/// (the part is ticked, a row is not); <c>KeptPartOff</c> is the part-level one (the part itself is unticked, so every folder stays).</summary>
internal readonly record struct ResetFolderLines(
    bool NoFolders, bool Unreadable, bool Kept, bool KeptPartOff, bool KeptSignIn, bool KeptSettings, bool KeptMcpSettings, bool KeptAdded);

/// <summary>
/// Reset's Claude Code folders while the dialog is open (Settings v2, slice 8; R12): every folder that holds Sanduhr's entries, ticked.
/// Lives as long as one dialog; Settings holds it meanwhile so ReloadNames renames its rows.
/// </summary>
internal sealed class ResetFolderListViewModel
{
    private ResetFolderListViewModel(IReadOnlyList<ResetFolderRow> rows, bool unreadable)
    {
        Rows = rows;
        ReceiptsUnreadable = unreadable;
        foreach (var row in rows)
            row.UndoChanged += () => Changed?.Invoke();
    }

    public IReadOnlyList<ResetFolderRow> Rows { get; }

    /// <summary>installs.json, or a copy set aside beside it, can't be read in full: Reset keeps every copy (decision 4).</summary>
    public bool ReceiptsUnreadable { get; }

    /// <summary>The folders the person unticked, with their kinds: what the service keeps per kind.</summary>
    public IReadOnlyList<ResetFolder> Kept => [.. Rows.Where(r => !r.Undo).Select(r => r.Folder)];

    /// <summary>The Claude Code part would change something: a ticked row, or no rows at all (Sanduhr's leftover copies still go).</summary>
    public bool UndoesAny => Rows.Count == 0 || Rows.Any(r => r.Undo);

    /// <summary>A row was ticked or unticked.</summary>
    public event Action? Changed;

    public void RefreshNames()
    {
        foreach (var row in Rows)
            row.RefreshName();
    }

    /// <summary>The lines for what is ticked. With the part ticked, the folders left as they are are the unticked rows; with it unticked,
    /// Reset leaves every folder alone, so every row counts as kept and the part-level line replaces the row-level one.</summary>
    public ResetFolderLines LinesFor(bool part, bool accounts, bool settings)
    {
        var kept = part ? Rows.Where(r => !r.Undo).ToList() : [.. Rows];
        bool meters = kept.Any(r => r.HasMeters);
        return new ResetFolderLines(
            NoFolders: Rows.Count == 0 && !ReceiptsUnreadable,
            Unreadable: ReceiptsUnreadable,
            Kept: part && kept.Count > 0,
            KeptPartOff: !part && kept.Count > 0,
            KeptSignIn: meters && accounts,
            KeptSettings: meters && settings,
            KeptMcpSettings: ResetService.KeptMcpLosesItsSettings(new ResetChoices(true, accounts, false, false, settings), [.. kept.Select(r => r.Folder)]),
            KeptAdded: settings && kept.Any(r => r.Added));
    }

    /// <summary>A list from rows already made (tests).</summary>
    internal static ResetFolderListViewModel For(IReadOnlyList<ResetFolderRow> rows, bool unreadable) => new(rows, unreadable);

    /// <summary>The folders as Reset finds them now: the receipts, plus Sanduhr's MCP entry and statusline in each discovered folder
    /// (decision 1). A read that throws reads as unreadable, so Reset keeps everything rather than guess.</summary>
    public static ResetFolderListViewModel Read(WidgetViewModel widget)
    {
        try
        {
            var profile = ClaudeCodeProfile.Dir;   // streamer: key - the profile Reset's folder list is built under, never drawn
            var added = widget.LoadCcFoldersAdded();
            var known = ClaudeCodeFolders.Build(profile, ClaudeCodeProfile.ConfigDir, added);   // streamer: key - folder list for Reset's rows; each row draws through ShowFolder
            string receipts = Path.Combine(widget.SanduhrAppDataDir, ResetPlan.IntegrationsName, ResetPlan.ReceiptsFileName);
            var ledger = new ReceiptStore(receipts).Load();
            var mcp = new McpIntegrationInstaller(profile, widget.SanduhrAppDataDir, externalDir: widget.SanduhrExternalDir);
            var statusline = new StatuslineInstaller(profile, widget.StatuslineBinDir, null, widget.SanduhrAppDataDir);
            var folders = ResetPlan.FoldersWithEntries(ledger.Records, known, k => k.Discovered && mcp.IsRegistered(k.Name), k => k.Discovered && statusline.IsRegistered(k.Name));
            var rows = folders.Select(f => new ResetFolderRow(
                f,
                known.Any(k => k.Discovered && ClaudeCodeFolders.Same(k.Path, f.Path)),   // streamer: key - the real folder each row keys on; the row draws it through ShowFolder
                added.Any(a => ClaudeCodeFolders.Same(a, f.Path)))).ToList();
            // The one shared rule, read from the widget's folder so tests stay in their scratch tree.
            bool unreadable = ResetPlan.ReceiptsUnreadable(receipts);
            return new ResetFolderListViewModel(rows, unreadable);
        }
        catch (Exception)
        {
            return new ResetFolderListViewModel([], true);
        }
    }
}
