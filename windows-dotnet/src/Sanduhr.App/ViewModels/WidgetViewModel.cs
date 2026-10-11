using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Services;
using Sanduhr.App.Theming;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// The floating widget's brain, ported from <c>widget.py</c>'s orchestration:
/// owns the tier cards, the active-account credential, the fetch loop, and the
/// two timers (5-min refetch + 30-s countdown re-render). Binds straight to the
/// Core <see cref="UsageFetcher"/> — the View is a thin projection of this.
///
/// Startup path (Milestone B): load the active account's stored credential via
/// <see cref="CredentialStore.Load"/>, construct a <see cref="ClaudeApiClient"/>
/// + <see cref="UsageFetcher"/>, and fetch — so the widget shows the user's REAL
/// usage immediately. The embedded WebView2 sign-in is item 6.
/// </summary>
public sealed partial class WidgetViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RiffInterval = TimeSpan.FromSeconds(4);

    private readonly Paths _paths;
    private readonly AccountStore _accounts;
    private readonly CredentialStore _credentials;
    private readonly UsageHistory _history;
    private readonly SettingsStore _settings;
    private readonly CcLogReader _ccReader;
    private readonly AlertEngine _alertEngine = new();
    private AlertConfig _alertConfig = new(true, 80, 95, true, false);
    private AlertService? _alertService;

    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _tickTimer;
    private readonly DispatcherTimer _riffTimer;

    private ThemePalette _palette = ThemePalette.Obsidian;
    private Dictionary<string, ThemeDefinition> _themes = new();
    private IClaudeApiClient? _client;
    private UsageFetcher? _fetcher;
    private JsonObject? _lastData;
    private DateTimeOffset? _lastFetchAt;
    private bool _refreshing;

    /// <summary>Demo mode's sample payload while it is on (never written anywhere).</summary>
    private JsonObject? _demoData;
    private string _demoAccount = DemoMode.Active;

    /// <summary>Demo mode: sample numbers for screenshots. Session-only; while on, nothing is
    /// fetched, and no snapshot, history, alert or limit state is written from the samples.</summary>
    [ObservableProperty] private bool _isDemo;

    /// <summary>The widget's demo banner.</summary>
    public string DemoBanner => DemoMode.Banner;

    /// <summary>The demo account showing (null when demo mode is off), for the tour's chip preview.</summary>
    public string? DemoAccount => IsDemo ? _demoAccount : null;

    /// <summary>What the cards show: the samples in demo mode, else the last fetch.</summary>
    private JsonObject? ShownData => IsDemo ? _demoData : _lastData;

    /// <summary>A successful real fetch rendered (the tour waits for the first one).</summary>
    public event Action? FetchSucceeded;

    /// <summary>Bumped on every <see cref="RebuildFetcher"/> call (account switch,
    /// sign-in reload, sign-out). Guards the post-switch retry in
    /// <see cref="RetryOnceIfTransientAsync"/> — if another rebuild lands during the
    /// retry's delay, the captured generation is stale and the retry is skipped.</summary>
    private int _fetcherGeneration;

    private readonly List<string> _savedOrder;
    private readonly HashSet<string> _hidden;
    private LimitState _limitState;
    private readonly HashSet<string> _mutedTiers;
    private IReadOnlyList<string> _riffs = Array.Empty<string>();
    private int _riffIndex;

    public ObservableCollection<TierCardViewModel> Tiers { get; } = new();

    /// <summary>The theme swatch entries — built-ins (in display order) then any
    /// loaded user themes, each marked active or not. This SAME collection backs
    /// both the widget's quick-switch swatch grid and Settings → Themes (the
    /// Settings tab exposes it via <c>ThemesViewModel.Themes</c>), so a switch in
    /// one immediately reflects the active highlight in the other. Each tile's
    /// click routes through its own <c>ApplyCommand</c> into <see cref="ApplySwatch"/>.</summary>
    public ObservableCollection<ThemeStripItemViewModel> Themes { get; } = new();

    [ObservableProperty] private string _statusText = "Connecting…";
    [ObservableProperty] private bool _pinned;
    [ObservableProperty] private bool _isCompact;

    /// <summary>Widget "Themes" header expand/collapse state — false (collapsed) by
    /// default so the swatch grid is tucked away under the thin header at rest.
    /// Bound to the grid's Visibility (BooleanToVisibilityConverter) and persisted to
    /// settings.json ("themes_expanded") so it sticks across launches. Settings →
    /// Themes is unaffected — that tab stays fully expanded.</summary>
    [ObservableProperty] private bool _isThemesExpanded;

    /// <summary>Active theme display name (e.g. "Matrix") — shown next to the
    /// collapsed "Themes" header so the resting state still names the live theme.</summary>
    [ObservableProperty] private string _activeThemeName = "";

    /// <summary>Display text for the content-area account switcher (widget.py
    /// <c>_active_account_text()</c> parity): empty when signed out, the bare label
    /// with one account, and <c>⇆ {active}</c> with 2+ (the ⇆ prefix signals a click
    /// cycles to the next account). Drives the switcher's visibility.</summary>
    [ObservableProperty] private string _accountSwitcherText = "";

    /// <summary>True when 2+ accounts are configured, so a click on the switcher
    /// actually cycles. Drives the pointing-hand cursor + hover affordance; with
    /// a single account the switcher is a flat, non-interactive label.</summary>
    [ObservableProperty] private bool _accountSwitcherClickable;

    [ObservableProperty] private string _footerText = "";
    [ObservableProperty] private string _planBadgeName = "";
    [ObservableProperty] private string _planTooltip = "";
    [ObservableProperty] private bool _hasPlanBadge;

    /// <summary>Why the widget is showing a sign-in recovery card (None = hidden).
    /// Drives the card's visibility, copy, and which action its primary button fires:
    /// FirstRun → add-account sign-in; Expired/Blocked → in-place re-auth.</summary>
    [ObservableProperty] private SignInReason _reason = SignInReason.None;

    /// <summary>Active sparkline style, cycled by the Graph control. Seeded from
    /// settings in the ctor; drives both the live card render and the toolbar glyph.</summary>
    [ObservableProperty] private SparklineStyle _sparklineStyle = SparklineStyle.Classic;

    /// <summary>Not now on the first-run card: put away for this launch only (in memory), so
    /// the next launch without an account asks again. The "Not signed in" line stays the way in.</summary>
    [ObservableProperty] private bool _signInPutOff;

    /// <summary>Visibility of the recovery card: any non-None reason shows it, except a
    /// first-run card put away with Not now.</summary>
    public bool ShowSignInPrompt => Reason != SignInReason.None && !ShowNotSignedInLine;

    /// <summary>The first-run card's Not now link (first run only; a recovery card stays).</summary>
    public bool ShowNotNow => Reason == SignInReason.FirstRun;

    /// <summary>The one-line "Not signed in" way in, shown after Not now.</summary>
    public bool ShowNotSignedInLine => Reason == SignInReason.FirstRun && SignInPutOff;

    partial void OnSignInPutOffChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowSignInPrompt));
        OnPropertyChanged(nameof(ShowNotSignedInLine));
    }

    /// <summary>Not now: the first-run card goes for this launch; nothing is chosen or saved.</summary>
    [RelayCommand]
    private void NotNow() => SignInPutOff = true;

    /// <summary>The active account's credential origin — routes the recovery card.
    /// Embedded when signed out (FirstRun copy doesn't branch on origin anyway).</summary>
    private AccountOrigin ActiveOrigin
        => _accounts.GetActive() is { } label ? _accounts.GetOrigin(label) : AccountOrigin.Embedded;

    /// <summary>Card headline for the current reason (empty when hidden).</summary>
    public string PromptHeadline => Reason == SignInReason.None ? "" : SignInPromptCopy.For(Reason, ActiveOrigin).Headline;
    /// <summary>Card subtitle for the current reason (empty when hidden).</summary>
    public string PromptSubtitle => Reason == SignInReason.None ? "" : SignInPromptCopy.For(Reason, ActiveOrigin).Subtitle;
    /// <summary>Primary-button label for the current reason (empty when hidden).</summary>
    public string PromptPrimaryLabel => Reason == SignInReason.None ? "" : SignInPromptCopy.For(Reason, ActiveOrigin).PrimaryLabel;
    /// <summary>Secondary-link label for the current reason (empty when hidden).</summary>
    public string PromptSecondaryLabel => Reason == SignInReason.None ? "" : SignInPromptCopy.For(Reason, ActiveOrigin).SecondaryLabel;

    partial void OnReasonChanged(SignInReason value)
    {
        OnPropertyChanged(nameof(ShowSignInPrompt));
        OnPropertyChanged(nameof(ShowNotNow));
        OnPropertyChanged(nameof(ShowNotSignedInLine));
        NotifyPromptCopy();
    }

    /// <summary>Re-raise the recovery-card copy bindings. Needed beyond
    /// OnReasonChanged because the copy also depends on ActiveOrigin: switching
    /// between two accounts that are BOTH Expired/Blocked reassigns Reason to the
    /// same value (no setter notification), yet the origin — and therefore the
    /// card's copy — may have changed.</summary>
    private void NotifyPromptCopy()
    {
        OnPropertyChanged(nameof(PromptHeadline));
        OnPropertyChanged(nameof(PromptSubtitle));
        OnPropertyChanged(nameof(PromptPrimaryLabel));
        OnPropertyChanged(nameof(PromptSecondaryLabel));
    }

    /// <summary>The tray reading (percent + tooltip label) under the chosen
    /// <see cref="TrayMode"/>; null when there is no data ("--").</summary>
    public event Action<TrayReading?>? TrayReadingChanged;

    private TrayMode _trayMode;
    private int _trayStep;
    private double? _traySession;
    private double? _trayWeekly;
    private (string Label, double Util)? _trayHighest;

    /// <summary>Which number the tray icon shows. Setting it persists, recomputes
    /// the reading from the last inputs (no fetch) and raises
    /// <see cref="TrayReadingChanged"/>.</summary>
    public TrayMode TrayMode
    {
        get => _trayMode;
        set
        {
            if (_trayMode == value) return;
            _trayMode = value;
            _settings.SaveTrayMode(value);
            OnPropertyChanged();
            RaiseTray();
        }
    }

    /// <summary>Advance the Rotate step (session, weekly, session...) and re-raise.</summary>
    public void AdvanceTrayRotation()
    {
        _trayStep++;
        RaiseTray();
    }

    /// <summary>What the tray shows now: Settings, General's preview reads it.</summary>
    public TrayReading? CurrentTrayReading =>
        TrayReadout.Reading(_trayMode, _traySession, _trayWeekly, _trayHighest, _trayStep);

    private void RaiseTray() => TrayReadingChanged?.Invoke(CurrentTrayReading);

    /// <summary>No data: forget the inputs and tell the tray to show "--".</summary>
    private void ClearTray()
    {
        _traySession = null;
        _trayWeekly = null;
        _trayHighest = null;
        TrayReadingChanged?.Invoke(null);
    }

    /// <summary>Raised when compact mode toggles — the window hides its toolbar and
    /// auto-sizes its height to the single remaining card.</summary>
    public event Action<bool>? CompactChanged;

    /// <summary>Raised by the "Sign in to Claude" command — App opens the embedded
    /// WebView2 <c>SignInWindow</c> flow, then calls <see cref="ReloadAfterSignInAsync"/>.</summary>
    public event Func<Task>? SignInRequested;

    /// <summary>Raised by the recovery card when the ACTIVE account needs re-auth
    /// (Expired/Blocked) — App routes this to <c>SignInCoordinator.ReauthenticateActiveAsync</c>,
    /// which refreshes the existing account in place rather than adding a new one.</summary>
    public event Func<Task>? ReauthRequested;

    /// <summary>Raised when the ACTIVE account needs an IN-PLACE manual key paste
    /// (manual-origin account expired, or "Paste a key instead" during recovery) —
    /// App routes this to <c>SignInCoordinator.ReauthenticateManualActiveAsync</c>.
    /// Distinct from <see cref="PasteKeyRequested"/>, which ADDS a new account.</summary>
    public event Func<Task>? ManualReauthRequested;

    /// <summary>Raised by the "Paste a key instead" command — App opens the manual
    /// sessionKey fallback modal.</summary>
    public event Func<Task>? PasteKeyRequested;

    /// <summary>Raised by the "Settings…" command (title-bar chip, widget right-click,
    /// tray) — App opens the <c>SettingsWindow</c> on the Accounts tab.</summary>
    public event Func<Task>? SettingsRequested;

    /// <summary>Raised by <see cref="OpenSettingsAtAsync"/> — App opens (or focuses) Settings
    /// and selects the named page.</summary>
    public event Func<SettingsPage, Task>? SettingsPageRequested;

    /// <summary>Open Settings on a named page (the tier card's "Alerts settings…").</summary>
    public async Task OpenSettingsAtAsync(SettingsPage page)
    {
        if (SettingsPageRequested is not null)
            await SettingsPageRequested.Invoke(page);
        else if (SettingsRequested is not null)
            await SettingsRequested.Invoke();
    }

    /// <summary>A "<c>Page</c> settings…" menu item: Settings on that page.</summary>
    [RelayCommand]
    private Task OpenSettingsPage(SettingsPage page) => OpenSettingsAtAsync(page);

    /// <summary>Raised after any account-registry mutation routed through this VM
    /// (switch / rename / sign-out). Open menus + the Settings accounts list re-read
    /// the registry so their checkmarks and rows stay in sync.</summary>
    public event Action? AccountsChanged;

    /// <summary>Raised after a theme is applied (startup or a live switch) so the
    /// window can swap its backdrop — Mica for glass themes, solid for a Matrix-
    /// style opt-out theme. The HWND work lives in the View; the palette is the
    /// payload.</summary>
    public event Action<ThemePalette>? ThemeChanged;

    /// <summary>Raised by the Focus affordance (title-bar button, right-click,
    /// tray, Ctrl+P) — the window toggles focus mode in/out, replacing the tier
    /// cards with the hourglass while active. Timer/render plumbing lives in the
    /// View; this is the entry point (port of <c>widget._toggle_focus_mode</c>).</summary>
    public event Action? FocusToggleRequested;

    /// <summary>Raised by the Game affordance — the window shows the cooldown snake
    /// overlay (port of <c>widget</c>'s <c>_btn_snake</c> → <c>start_game</c>).</summary>
    public event Action? GameStartRequested;

    /// <summary>Raised when any alert preference is saved — the alert pipeline
    /// re-reads its config on the next evaluation.</summary>
    public event Action? AlertSettingsChanged;

    public ThemePalette Palette => _palette;

    /// <summary>The user theme drop-in folder, for the Settings "Open themes folder"
    /// and save/reload flows.</summary>
    public string ThemesDir => _paths.ThemesDir;

    /// <summary>True when <paramref name="key"/> resolves to a loaded theme
    /// (built-in or user). Lets the Settings tab guard Apply before switching.</summary>
    public bool HasTheme(string key) => _themes.ContainsKey(key);

    /// <summary>Account labels in registry order — drives the quick-switch menus.</summary>
    public IReadOnlyList<string> ListAccounts() => _accounts.ListAccounts();

    /// <summary>The active account label, or null when signed out.</summary>
    public string? ActiveAccount => _accounts.GetActive();

    /// <summary>The accounts a surface shows: the sample accounts while demo mode runs, so no
    /// real label reaches Settings or a menu in a screenshot; the registry otherwise.</summary>
    public IReadOnlyList<string> ShownAccounts => IsDemo ? DemoMode.Accounts : ListAccounts();

    /// <summary>The account a surface marks as active: the sample one in demo mode.</summary>
    public string? ShownActiveAccount => IsDemo ? _demoAccount : ActiveAccount;

    /// <summary>Demo mode: show another sample account (the menus' sample entries). Ignored
    /// outside demo mode and for a label that is not a sample.</summary>
    [RelayCommand]
    private void ShowSampleAccount(string? label)
    {
        if (!IsDemo || label is null || !DemoMode.Accounts.Contains(label) || label == _demoAccount)
            return;
        _demoAccount = label;
        ShowDemoAccount();
    }

    /// <summary>The per-account usage history store — shared with the Settings
    /// History tab (chart + CSV export) so it reads the same files the live
    /// widget writes.</summary>
    public UsageHistory History => _history;

    /// <summary>The multi-account registry — the Settings History tab's account
    /// selector + CSV export read account labels from here.</summary>
    public AccountStore AccountStore => _accounts;

    /// <summary>The local Claude Code session-log reader — shared with the
    /// Settings Local CC tab so it reuses the same 30-second aggregation cache the
    /// card badges hit.</summary>
    public CcLogReader CcReader => _ccReader;

    /// <summary>The Local CC tab's "show breakdowns" preference (settings.json).</summary>
    public bool LoadLocalCcShowBreakdowns() => _settings.LoadLocalCcShowBreakdowns();

    /// <summary>Persist the Local CC tab's "show breakdowns" preference.</summary>
    public void SaveLocalCcShowBreakdowns(bool show) => _settings.SaveLocalCcShowBreakdowns(show);

    /// <summary>The Sessions Ledger's "stack by project" preference (settings.json).</summary>
    public bool LoadLedgerGroupByProject() => _settings.LoadLedgerGroupByProject();

    /// <summary>Persist the Ledger's "stack by project" preference.</summary>
    public void SaveLedgerGroupByProject(bool groupByProject) => _settings.SaveLedgerGroupByProject(groupByProject);

    /// <summary>The saved focus-timer duration (settings.json "focus_mode_duration").</summary>
    public int LoadFocusDuration() => _settings.LoadFocusDuration();

    /// <summary>Persist the focus-timer duration chosen for a session.</summary>
    public void SaveFocusDuration(int minutes) => _settings.SaveFocusDuration(minutes);

    /// <summary>The saved cooldown-snake high score (settings.json "snake_high_score").</summary>
    public int LoadSnakeHighScore() => _settings.LoadSnakeHighScore();

    /// <summary>Persist a new cooldown-snake high score.</summary>
    public void SaveSnakeHighScore(int score) => _settings.SaveSnakeHighScore(score);

    /// <summary>Alert preferences (settings.json, WS-B). Saves raise
    /// <see cref="AlertSettingsChanged"/> so the live engine re-reads config.</summary>
    public AlertConfig LoadAlertConfig() => _settings.LoadAlertConfig();

    public void SaveAlertConfig(AlertConfig config)
    {
        _settings.SaveAlertConfig(config);
        AlertSettingsChanged?.Invoke();
    }

    public bool LoadAlertSound() => _settings.LoadAlertSound();

    public void SaveAlertSound(bool on)
    {
        _settings.SaveAlertSound(on);
        AlertSettingsChanged?.Invoke();
    }

    public bool LoadAlertSoundScoped() => _settings.LoadAlertSoundScoped();

    public void SaveAlertSoundScoped(bool on)
    {
        _settings.SaveAlertSoundScoped(on);
        AlertSettingsChanged?.Invoke();
    }

    public bool LoadAlertSnakeFull() => _settings.LoadAlertSnakeFull();

    public void SaveAlertSnakeFull(bool on)
    {
        _settings.SaveAlertSnakeFull(on);
        AlertSettingsChanged?.Invoke();
    }

    // -- WS-E statusline bridge -----------------------------------------------

    /// <summary>Whether the statusline snapshot writer is on (settings.json).</summary>
    public bool LoadStatuslineEnabled() => _settings.LoadStatuslineEnabled();

    /// <summary>Flip the snapshot writer. Enabling writes immediately from the
    /// cached fetch (the statusline lights up at the next prompt, not the next
    /// 5-minute poll); disabling deletes the file — consent revocation revokes
    /// the artifact, not just the writer.</summary>
    public void SaveStatuslineEnabled(bool on)
    {
        _settings.SaveStatuslineEnabled(on);
        _statuslineEnabled = on;
        if (!on)
            _snapshotWriter?.Delete();
        else if (_lastData is not null)
            WriteSnapshotOk(_lastData);
    }

    /// <summary>The CC home the statusline was installed into (removal targets
    /// the same home the consent chose).</summary>
    public string? LoadStatuslineCcHome() => _settings.LoadStatuslineCcHome();

    public void SaveStatuslineCcHome(string ccHomeName) => _settings.SaveStatuslineCcHome(ccHomeName);

    /// <summary>Where the statusline helper script installs — OUTSIDE the package
    /// trees so it survives app updates (the widget re-writes it on start).</summary>
    public string StatuslineBinDir => _paths.StatuslineBinDir;

    /// <summary>The Sanduhr app-data dir — the MCP installer's root for the
    /// launcher (bin\) and versioned server dirs (mcp\).</summary>
    public string SanduhrAppDataDir => _paths.AppDataDir;

    // -- WS-E MCP integration facade -------------------------------------------

    public bool LoadMcpEnabled() => _settings.LoadMcpEnabled();
    public void SaveMcpEnabled(bool on) => _settings.SaveMcpEnabled(on);
    public string? LoadMcpCcHome() => _settings.LoadMcpCcHome();
    public void SaveMcpCcHome(string ccHomeName) => _settings.SaveMcpCcHome(ccHomeName);
    public IReadOnlyDictionary<string, bool> LoadMcpRoots() => _settings.LoadMcpRoots();
    public void SaveMcpRoots(IReadOnlyDictionary<string, bool> roots) => _settings.SaveMcpRoots(roots);

    /// <summary>Write a status=ok snapshot from a successful fetch. No-op while
    /// the integration is off; never throws into the fetch path.</summary>
    private void WriteSnapshotOk(JsonObject data)
    {
        // Demo mode never reaches snapshot.json (the statusline and the MCP read it).
        if (!_statuslineEnabled || IsDemo)
            return;
        try
        {
            _snapshotWriter?.WriteOk(data, HasPlanBadge ? PlanBadgeName : null,
                _accounts.GetActive(), DateTimeOffset.UtcNow);
        }
        catch
        {
            // Snapshot is instrumentation for an external consumer — a failure
            // must never surface into the fetch/render cycle.
        }
    }

    /// <summary>Write a status=error snapshot (last-good tiers kept) so the
    /// statusline can say "reauth needed" instead of going silently stale.</summary>
    private void WriteSnapshotError(string errorKind)
    {
        if (!_statuslineEnabled || IsDemo)
            return;
        try
        {
            _snapshotWriter?.WriteError(errorKind, HasPlanBadge ? PlanBadgeName : null,
                _accounts.GetActive(), DateTimeOffset.UtcNow);
        }
        catch
        {
            // Same posture as WriteSnapshotOk.
        }
    }

    /// <summary>WS-A LogBestEffortFailure convention: operation + exception type
    /// only — no labels, no contents.</summary>
    private void LogSnapshotFailure(string line)
    {
        try
        {
            File.AppendAllText(_paths.LogFile, $"{DateTime.UtcNow:o} {line}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never break the caller.
        }
    }

    /// <summary>Alert delivery, attached by App once the main window exists
    /// (the service needs an activation callback). Null in unit contexts.</summary>
    public AlertService? AlertService => _alertService;

    public void AttachAlertService(AlertService service) => _alertService = service;

    private VaultService? _vault;
    private DateOnly _lastTickDate = DateOnly.FromDateTime(DateTime.Now);
    private long _ccDeltaTotal;
    private bool _ccScanRunning;

    /// <summary>WS-E statusline bridge: the one writer of snapshot.json. Gated
    /// by <see cref="_statuslineEnabled"/> — the file exists only while the
    /// integration is installed and on.</summary>
    private SnapshotWriter? _snapshotWriter;
    private bool _statuslineEnabled;

    /// <summary>The usage-vault service, attached by App AFTER the consent
    /// prompt resolves (never before — attach order IS the consent gate).
    /// Null in unit contexts.</summary>
    public VaultService? Vault => _vault;

    public void AttachVaultService(VaultService service) => _vault = service;

    private UsagePublishService? _publish;

    /// <summary>"Publish usage" (opt-in, user-chosen endpoint). Attached by App; its Tick rides
    /// the 30-second <see cref="OnTick"/> loop. Null in unit contexts.</summary>
    public UsagePublishService? Publish => _publish;

    public void AttachPublishService(UsagePublishService service) => _publish = service;

    private ThemeHandoffService? _themeHandoff;

    /// <summary>sanduhr-mcp's propose_theme handoff. Attached by App; watches the
    /// request file and also rides the 30-second tick. Null in unit contexts.</summary>
    public void AttachThemeHandoffService(ThemeHandoffService service) => _themeHandoff = service;

    private StoreUpdateService? _storeUpdate;

    /// <summary>Waiting-Store-update notice (packaged build only). Attached by App;
    /// rides the 30-second tick and acts at most every few hours. Null in unit
    /// contexts and inert on the Velopack build.</summary>
    public void AttachStoreUpdateService(StoreUpdateService service) => _storeUpdate = service;

    /// <summary>The catalog key of the live palette (what the strip marks active
    /// and settings.json remembers). The theme handoff reports it as
    /// previous_key so an agent can offer the way back.</summary>
    public string ActiveThemeKey => _palette.Key;

    private DispatcherTimer? _statusNoticeTimer;

    /// <summary>Show a message in the widget's status slot for a while, then clear
    /// it unless something else has written there since. Used for notices that
    /// are not errors: a theme applied from Claude Code, for one.</summary>
    public void ShowTransientStatus(string message, TimeSpan duration)
    {
        StatusText = message;
        _statusNoticeTimer?.Stop();
        _statusNoticeTimer = new DispatcherTimer { Interval = duration };
        _statusNoticeTimer.Tick += (_, _) =>
        {
            _statusNoticeTimer?.Stop();
            if (StatusText == message)
                StatusText = "";
        };
        _statusNoticeTimer.Start();
    }

    /// <summary>The widget's own version — stamped into snapshot.json as
    /// writer_version so readers can tell a dev build's file from the installed
    /// release's (Core's assembly is unversioned and would read "1.0.0").</summary>
    private static string AppVersion
    {
        get
        {
            var v = typeof(WidgetViewModel).Assembly.GetName().Version;
            return v is null ? "unknown" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    public WidgetViewModel()
    {
        _paths = new Paths();
        _accounts = new AccountStore(new WindowsCredentialManager(AccountStore.Service));
        _credentials = new CredentialStore(_accounts, _paths);
        _history = new UsageHistory(_accounts, _paths);
        _settings = new SettingsStore(_paths);
        _ccReader = new CcLogReader(null, () => ClaudeCodeHomes.Discover(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR")));

        _alertConfig = _settings.LoadAlertConfig();
        AlertSettingsChanged += () => _alertConfig = _settings.LoadAlertConfig();

        _snapshotWriter = new SnapshotWriter(_paths.SnapshotFile, LogSnapshotFailure, AppVersion);
        _statuslineEnabled = _settings.LoadStatuslineEnabled();

        _pinned = _settings.LoadPinned();
        _isThemesExpanded = _settings.LoadThemesExpanded();
        _savedOrder = _settings.LoadTierOrder().ToList();
        _hidden = new HashSet<string>(_settings.LoadHiddenTiers());
        _trayMode = _settings.LoadTrayMode();
        _limitState = _settings.LoadLimitState();
        _mutedTiers = new HashSet<string>(_settings.LoadAlertMutedTiers());

        _refreshTimer = new DispatcherTimer { Interval = RefreshInterval };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _tickTimer = new DispatcherTimer { Interval = TickInterval };
        _tickTimer.Tick += (_, _) => OnTick();
        _riffTimer = new DispatcherTimer { Interval = RiffInterval };
        _riffTimer.Tick += (_, _) => RotateRiff();

        // Resolve the saved theme (settings.json "theme" key), merge in user
        // drop-ins, and build the strip. The palette object is set here so the
        // window's first paint uses the right colors; App applies it to the
        // Application resources before Show (no obsidian->saved flash).
        RebuildThemeMap();
        var savedKey = _settings.LoadTheme();
        _sparklineStyle = _settings.LoadSparklineStyle();
        if (!_themes.TryGetValue(savedKey, out var def))
        {
            savedKey = "obsidian";
            def = _themes[savedKey];
        }
        _palette = new ThemePalette(savedKey, def);
        _activeThemeName = _palette.Name;
        BuildThemeStrip(savedKey);
    }

    /// <summary>Wire the active account, start the timers, and kick the first
    /// fetch. Called once after the window is shown.</summary>
    public async void Start()
    {
        // One-time legacy promotion BEFORE the first fetcher build: a pre-v2.2.0
        // single-slot keyring key / v1 plaintext config is promoted to a "Personal"
        // account so an upgrader isn't wrongly shown the first-run card. MigrateFromV1
        // / MigrateLegacy are idempotent (no-op on a populated registry) and swallow
        // their own Json/IO faults; guard anyway so a migration fault never blocks
        // first paint.
        try { _credentials.MigrateFromV1(); }
        catch (Exception ex) { Debug.WriteLine($"[Sanduhr.Migrate] skipped: {ex.Message}"); }

        RebuildFetcher();
        RefreshAccountLabel();
        _tickTimer.Start();
        _refreshTimer.Start();
        await RefreshAsync();
    }

    // -- theming --------------------------------------------------------------

    /// <summary>Push the active palette into the Application resources so every
    /// <c>{DynamicResource Sanduhr.*}</c> binding resolves. Called by App once
    /// before the window is shown (avoids an obsidian→saved-theme flash).</summary>
    public void ApplyThemeResources()
    {
        if (Application.Current is { } app)
            _palette.Apply(app.Resources);
    }

    /// <summary>Rebuild the merged theme map: the 6 built-ins plus any valid user
    /// drop-ins from <c>%APPDATA%\Sanduhr\themes</c> (user themes override a
    /// built-in of the same key). Invalid files are skipped + logged, 1:1 with
    /// <c>themes.load_user_themes</c>.</summary>
    private void RebuildThemeMap()
    {
        var map = new Dictionary<string, ThemeDefinition>(ThemeCatalog.BuiltIns);
        foreach (var (key, def) in ThemeCatalog.LoadUserThemes(_paths.ThemesDir, Log))
            map[key] = def;
        _themes = map;
    }

    /// <summary>Rebuild the swatch collection: built-ins in display order, then any
    /// user themes (sorted), with <paramref name="activeKey"/> marked active. Each
    /// tile gets the theme's own preview colors + an apply callback routed through
    /// <see cref="ApplySwatch"/>.</summary>
    private void BuildThemeStrip(string activeKey)
    {
        Themes.Clear();
        foreach (var key in ThemeCatalog.BuiltInOrder)
            if (_themes.TryGetValue(key, out var def))
                Themes.Add(new ThemeStripItemViewModel(key, def, key == activeKey, ApplySwatch));

        var userKeys = _themes.Keys
            .Where(k => !ThemeCatalog.BuiltIns.ContainsKey(k))
            .OrderBy(k => k, StringComparer.Ordinal);
        foreach (var key in userKeys)
            Themes.Add(new ThemeStripItemViewModel(key, _themes[key], key == activeKey, ApplySwatch));
    }

    /// <summary>Apply a theme by catalog key: re-tint the live resources, recolor
    /// existing cards, persist the choice, update the strip's active marker, and
    /// raise <see cref="ThemeChanged"/> so the window swaps its backdrop. Silent
    /// (no chime) — callers add their own cue (the strip plays a toggle tick; the
    /// Settings tab plays a save confirmation).</summary>
    public void ApplyThemeByKey(string key)
    {
        if (!_themes.TryGetValue(key, out var def))
            return;

        _previewing = false;
        _palette = new ThemePalette(key, def);
        ActiveThemeName = _palette.Name;
        ApplyThemeResources();

        foreach (var card in Tiers)
            card.ApplyPalette(_palette);

        _settings.SaveTheme(key);
        foreach (var item in Themes)
            item.IsActive = item.Key == key;

        ThemeChanged?.Invoke(_palette);
    }

    /// <summary>A swatch tile's click handler (widget or Settings) — apply the
    /// theme instantly and play a soft toggle tick. The visible re-tint is the real
    /// confirmation, mirroring the Python build's "the change IS the confirmation"
    /// pattern. Wired into every <see cref="ThemeStripItemViewModel.ApplyCommand"/>
    /// so both swatch grids route through one path and stay in sync.</summary>
    private void ApplySwatch(string key)
    {
        if (string.IsNullOrEmpty(key))
            return;
        ApplyThemeByKey(key);
        Sounds.PlayToggle();
    }

    private bool _previewing;

    /// <summary>The Theme Studio's live preview: re-tint the widget from a draft
    /// without touching the saved theme, the strip's active tile, or settings.
    /// <see cref="EndPreview"/> (or any real apply) puts the saved theme back.</summary>
    public void PreviewTheme(ThemeDefinition def)
    {
        _previewing = true;
        var preview = new ThemePalette("studio", def);
        if (Application.Current is { } app)
            preview.Apply(app.Resources);
        foreach (var card in Tiers)
            card.ApplyPalette(preview);
        ThemeChanged?.Invoke(preview);
    }

    /// <summary>Drop a studio preview and paint the saved theme again. No-op
    /// when nothing is being previewed.</summary>
    public void EndPreview()
    {
        if (!_previewing)
            return;
        _previewing = false;
        ApplyThemeResources();
        foreach (var card in Tiers)
            card.ApplyPalette(_palette);
        ThemeChanged?.Invoke(_palette);
    }

    /// <summary>A theme's definition by catalog key (built-in or loaded user
    /// theme), for the studio's "Start from". Null when unknown.</summary>
    public ThemeDefinition? GetThemeDefinition(string key)
        => _themes.TryGetValue(key, out var def) ? def : null;

    public bool LoadThemesStudioMode() => _settings.LoadThemesStudioMode();
    public void SaveThemesStudioMode(bool on) => _settings.SaveThemesStudioMode(on);

    /// <summary>Reload user theme drop-ins from disk and rebuild the strip,
    /// keeping the current theme active. Called from the Settings Themes tab after
    /// a save / reload / delete.</summary>
    public void ReloadUserThemes()
    {
        RebuildThemeMap();
        BuildThemeStrip(_palette.Key);
    }

    private static void Log(string message) => Debug.WriteLine($"[Sanduhr.Themes] {message}");

    private void RebuildFetcher()
    {
        _fetcherGeneration++;
        (_client as IDisposable)?.Dispose();
        _client = null;
        _fetcher = null;

        var creds = _credentials.Load();
        if (string.IsNullOrEmpty(creds.SessionKey))
        {
            // No active account → first-run prompt. Wipe any signed-in chrome too, so a
            // sign-out (or a removed last account) doesn't leave a stale plan badge,
            // account label, or footer on screen.
            Reason = SignInReason.FirstRun;
            StatusText = "";
            ClearSignedInChrome();
            ClearTray();
            return;
        }
        Reason = SignInReason.None;
        // WebView2-backed transport (item 3 pivot): a raw HttpClient can't pass
        // Cloudflare's fingerprint binding, but a real authenticated browser can.
        // Same (sessionKey, cfClearance) shape as ClaudeApiClient, same interface.
        _client = new WebView2ApiClient(creds.SessionKey, creds.CfClearance);
        _fetcher = new UsageFetcher(_client, _history);
    }

    // -- demo mode -------------------------------------------------------------

    /// <summary>Demo mode on: pause fetches and show the Work sample account. The real
    /// account, credentials and files are untouched; a fetch already in flight is dropped.</summary>
    public void StartDemo()
    {
        if (IsDemo)
            return;
        IsDemo = true;
        _refreshTimer.Stop();
        _alertEngine.Reset();
        Reason = SignInReason.None;
        StatusText = "";
        _demoAccount = DemoMode.Active;
        ShowDemoAccount();
    }

    /// <summary>Demo mode off: the real state back, fetched afresh.</summary>
    [RelayCommand]
    public async Task EndDemoAsync()
    {
        if (!IsDemo)
            return;
        LeaveDemo();
        RebuildFetcher();
        RefreshAccountLabel();
        var outcome = await RefreshCoreAsync().ConfigureAwait(true);
        await RetryOnceIfTransientAsync(outcome).ConfigureAwait(true);
    }

    /// <summary>Drop the samples and restart the fetch timer; the caller refetches.</summary>
    private void LeaveDemo()
    {
        IsDemo = false;
        _demoData = null;
        _lastData = null;
        Tiers.Clear();
        ClearTray();   // the sample percent never lingers on the tray unlabelled
        FooterText = "";
        OnPropertyChanged(nameof(DemoAccount));
        _refreshTimer.Start();
        AccountsChanged?.Invoke();   // Settings and menus go back to the real accounts
    }

    /// <summary>Render the current sample account: cards, tray, chip and footer.</summary>
    private void ShowDemoAccount()
    {
        var now = DateTimeOffset.UtcNow;
        _demoData = DemoMode.Usage(_demoAccount, now);
        Tiers.Clear();   // fresh cards: no real Claude Code burn carried over
        RenderCards(_demoData, now);
        HasPlanBadge = false;
        PlanBadgeName = "";
        PlanTooltip = "";
        _riffTimer.Stop();
        RefreshAccountLabel();
        UpdateFooter();
        OnPropertyChanged(nameof(DemoAccount));
        AccountsChanged?.Invoke();   // Settings and menus show the samples
    }

    /// <summary>Rebuild the fetcher against the now-active account, refresh the
    /// account label, and kick an immediate fetch — called after a successful
    /// embedded or manual sign-in so the widget starts tracking at once.</summary>
    public async Task ReloadAfterSignInAsync()
    {
        // Null first, parity with SwitchAccount: an add-account sign-in SetActive's the
        // new account before this runs, so a stale _lastData from the PRIOR account would
        // make RetryOnceIfTransientAsync's "_lastData is not null" guard skip the retry —
        // the exact cold-transport/CF race this method exists to cover.
        if (IsDemo)
            LeaveDemo();   // a real sign-in ends demo mode
        _lastData = null;
        _snapshotWriter?.Delete();   // same no-bleed rule as SwitchAccount
        RebuildFetcher();
        RefreshAccountLabel();
        AccountsChanged?.Invoke();
        var outcome = await RefreshCoreAsync().ConfigureAwait(true);
        await RetryOnceIfTransientAsync(outcome).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task SignIn()
    {
        if (SignInRequested is not null)
            await SignInRequested.Invoke();
    }

    /// <summary>The recovery card's primary button, routed by the origin-aware
    /// table: FirstRun → add-account sign-in; Expired/Blocked → in-place re-auth
    /// via the method that matches how the account was created.</summary>
    [RelayCommand]
    private async Task PrimaryAuth()
    {
        switch (ReauthRouting.Primary(Reason, ActiveOrigin))
        {
            case AuthFlow.ManualReauth:
                if (ManualReauthRequested is not null) await ManualReauthRequested.Invoke();
                break;
            case AuthFlow.EmbeddedReauth:
                if (ReauthRequested is not null) await ReauthRequested.Invoke();
                break;
            default: // EmbeddedAdd (FirstRun)
                if (SignInRequested is not null) await SignInRequested.Invoke();
                break;
        }
    }

    /// <summary>The recovery card's secondary link — always the OTHER method. In
    /// recovery it re-auths IN PLACE (the pre-WS-A behavior of adding a duplicate
    /// "Account N" here was a bug); on FirstRun it stays a manual add.</summary>
    [RelayCommand]
    private async Task SecondaryAuth()
    {
        switch (ReauthRouting.Secondary(Reason, ActiveOrigin))
        {
            case AuthFlow.ManualReauth:
                if (ManualReauthRequested is not null) await ManualReauthRequested.Invoke();
                break;
            case AuthFlow.EmbeddedReauth:
                if (ReauthRequested is not null) await ReauthRequested.Invoke();
                break;
            default: // ManualAdd (FirstRun)
                if (PasteKeyRequested is not null) await PasteKeyRequested.Invoke();
                break;
        }
    }

    [RelayCommand]
    private async Task OpenSettings()
    {
        if (SettingsRequested is not null)
            await SettingsRequested.Invoke();
    }

    /// <summary>
    /// Quick-switch the active account (title-bar chip, widget right-click submenu,
    /// tray submenu, or the Settings "Switch to" button all route here). Sets the
    /// active pointer, rebuilds the fetcher against the new account — which spins up
    /// a fresh <see cref="WebView2ApiClient"/> whose <c>InitAsync</c> wipes the shared
    /// transport cookie jar before injecting the new sessionKey, so the next fetch
    /// returns the NEW account's usage with no bleed — then refetches. Tier cards are
    /// cleared first so the old account's numbers never linger during the switch. If
    /// that first refetch comes back TRANSIENT (the CF re-clearance race — see
    /// <see cref="RetryOnceIfTransientAsync"/>), one delayed retry follows.
    /// </summary>
    [RelayCommand]
    private async Task SwitchAccount(string? label)
    {
        if (string.IsNullOrEmpty(label) || (label == _accounts.GetActive() && !IsDemo))
            return;
        if (IsDemo)
            LeaveDemo();   // picking a real account ends demo mode

        _accounts.SetActive(label);
        // Snapshot is deleted synchronously BEFORE the switch completes — no
        // window where the new account's freshness wraps the old account's
        // numbers (WS-E). The next successful fetch rewrites it.
        _snapshotWriter?.Delete();
        Tiers.Clear();
        _alertEngine.Reset();
        _lastData = null;
        StatusText = "Switching account…";
        ClearTray();

        RebuildFetcher();
        RefreshAccountLabel();
        AccountsChanged?.Invoke();
        var outcome = await RefreshCoreAsync().ConfigureAwait(true);
        await RetryOnceIfTransientAsync(outcome).ConfigureAwait(true);
    }

    /// <summary>
    /// Round-robin the active account to the NEXT registered one (wrap around) and
    /// re-fetch — the content-area account switcher's click handler. Ports
    /// <c>widget.py</c>'s <c>_cycle_active_account</c>: a no-op with fewer than two
    /// accounts. Delegates to <see cref="SwitchAccount"/> so the tier-card clear +
    /// anti-bleed cookie wipe + refetch all apply identically.
    /// </summary>
    [RelayCommand]
    private async Task CycleAccount()
    {
        if (IsDemo)
        {
            // Demo mode: the chip moves among the sample accounts only.
            _demoAccount = DemoMode.Next(_demoAccount);
            ShowDemoAccount();
            return;
        }
        var labels = _accounts.ListAccounts().ToList();
        if (labels.Count < 2)
            return;
        var active = _accounts.GetActive();
        if (active is null)
            return;
        int idx = labels.IndexOf(active);
        if (idx < 0)
            return;
        var next = labels[(idx + 1) % labels.Count];
        await SwitchAccount(next);
    }

    /// <summary>Rename an account in place (same secrets, new label). The active
    /// pointer follows the rename inside <see cref="AccountStore"/>, and the
    /// history file moves with it so the chart survives the rename.</summary>
    public void RenameAccount(string oldLabel, string newLabel)
    {
        _accounts.RenameAccount(oldLabel, newLabel);
        _history.Rename(oldLabel, newLabel);
        RefreshAccountLabel();
        AccountsChanged?.Invoke();
    }

    /// <summary>
    /// Remove an account completely: unlink its per-account history file, drop its
    /// Credential-Manager slots (sessionKey / cf_clearance / origin), and — when it
    /// was the active one — <see cref="AccountStore.RemoveAccount"/> advances the
    /// active pointer to the first remaining account (or none). Removing the LAST
    /// account also purges the shared webview2-fetch transport profile: no future
    /// client init would ever run its anti-bleed cookie wipe, so the deleted
    /// account's live claude.ai cookies would otherwise sit on disk indefinitely.
    /// </summary>
    public async Task SignOutAccountAsync(string label)
    {
        // Sign-out ends demo mode without fetching for the account about to go; the
        // active-account path below rebuilds and refetches, the other path does it here.
        bool leftDemo = IsDemo;
        if (leftDemo)
            LeaveDemo();
        if (leftDemo && _accounts.GetActive() != label)
        {
            RebuildFetcher();
            RefreshAccountLabel();
            _ = RefreshAsync();
        }
        bool wasActive = _accounts.GetActive() == label;
        if (wasActive)
        {
            // Release the transport's hold on the shared profile BEFORE cleanup.
            (_client as IDisposable)?.Dispose();
            _client = null;
            _fetcher = null;
        }
        _history.Delete(label);
        _accounts.RemoveAccount(label);

        if (wasActive)
        {
            Tiers.Clear();
            _lastData = null;
            // The snapshot belongs to the active account — removal revokes the
            // artifact, not just the writer (WS-E lifecycle-delete rule).
            _snapshotWriter?.Delete();
            if (_accounts.ListAccounts().Count == 0)
                await PurgeTransportProfileBestEffortAsync();
            RebuildFetcher();
            await RefreshAsync();
        }
        RefreshAccountLabel();
        AccountsChanged?.Invoke();
    }

    /// <summary>Delete the shared webview2-fetch profile directory. WebView2's
    /// browser processes release their file locks asynchronously after Dispose,
    /// so retry briefly; a stubborn lock is logged and left for the next client
    /// init's cookie wipe (best-effort — the app must keep working regardless).</summary>
    private async Task PurgeTransportProfileBestEffortAsync()
    {
        var profile = _paths.WebView2FetchDir;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (Directory.Exists(profile))
                    Directory.Delete(profile, recursive: true);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(300);
            }
        }
        try
        {
            File.AppendAllText(_paths.LogFile,
                $"{DateTime.UtcNow:o} webview2-fetch purge failed — cookies remain until the next transport init{Environment.NewLine}");
        }
        catch
        {
            // Swallow — logging must not break the removal flow.
        }
    }

    /// <summary>How a <see cref="RefreshCoreAsync"/> attempt ended, so callers that
    /// need to react (the post-switch retry) don't have to re-parse exception types
    /// or status text. <c>Ok</c> = fresh data rendered; <c>Transient</c> = a
    /// network-shaped failure (timeout / connection) that's worth one quick retry;
    /// <c>Auth</c> = a recovery card is now showing (never retried here — that's
    /// the user's job); <c>Other</c> = any other failure, or the single-flight/no-
    /// fetcher guard tripped (nothing ran).</summary>
    private enum RefreshOutcome { Ok, Transient, Auth, Other }

    [RelayCommand]
    private async Task RefreshAsync() => await RefreshCoreAsync().ConfigureAwait(true);

    /// <summary>The actual fetch-and-render cycle, factored out of the public
    /// <see cref="RefreshAsync"/> command so callers that need to know HOW it ended
    /// (the post-switch retry in <see cref="RetryOnceIfTransientAsync"/>) can react
    /// without re-deriving it from status text. Behavior is unchanged from the
    /// pre-split RefreshAsync for every existing caller — same status texts, same
    /// recovery entries, same single-flight guard via <see cref="_refreshing"/>.</summary>
    private async Task<RefreshOutcome> RefreshCoreAsync()
    {
        // Vault ingest rides the 5-min cycle: fire-and-forget, single-flight,
        // never awaited (spec: Ingestion). Runs even when signed out — the
        // Local CC surfaces work without an account.
        _vault?.TriggerIngest();

        // Demo mode pauses fetches; a real answer must never replace the samples.
        if (_fetcher is null || _refreshing || IsDemo)
            return RefreshOutcome.Other;
        _refreshing = true;
        int generation = _fetcherGeneration;
        try
        {
            if (Tiers.Count == 0)
                StatusText = "Refreshing…";
            var data = await _fetcher.FetchAsync().ConfigureAwait(true);
            // Demo mode came on, or the fetcher was rebuilt (demo ended, account switched),
            // while this fetch was out: drop the answer. The rebuild's own fetch follows.
            if (!DemoMode.AnswerApplies(generation == _fetcherGeneration, IsDemo))
                return RefreshOutcome.Other;
            _lastData = data;
            _lastFetchAt = DateTimeOffset.Now;
            ReconcileLimits(data, DateTimeOffset.UtcNow);
            RenderCards(data, DateTimeOffset.UtcNow);
            EvaluateAlerts(data, DateTimeOffset.UtcNow);
            // Fresh fetch re-anchors the Local CC delta window: badges/footer
            // reset to ~0 now; the async scan grows them between fetches.
            _ccDeltaTotal = 0;
            foreach (var vm in Tiers)
                vm.SetLocalDelta(0);
            _ = RefreshCcDerivedAsync();
            UpdatePlanBadge();
            StatusText = "";
            Reason = SignInReason.None;
            UpdateFooter();
            WriteSnapshotOk(data);
            FetchSucceeded?.Invoke();
            return RefreshOutcome.Ok;
        }
        catch (Exception) when (!DemoMode.AnswerApplies(generation == _fetcherGeneration, IsDemo))
        {
            // A failure that lands under demo mode, or from a fetcher since rebuilt, must not
            // wipe the samples or the new state, or write an error snapshot.
            return RefreshOutcome.Other;
        }
        catch (SessionExpiredException)
        {
            // Stored key rejected (non-empty, so RebuildFetcher's empty gate never fires):
            // show the actionable recovery card and wipe the now-stale signed-in chrome.
            WriteSnapshotError(SnapshotContract.ErrorSessionExpired);
            EnterRecoveryState(SignInReason.Expired);
            return RefreshOutcome.Auth;
        }
        catch (CloudflareBlockedException)
        {
            // Cloudflare challenge — re-auth re-captures a fresh cf_clearance silently.
            WriteSnapshotError(SnapshotContract.ErrorCloudflare);
            EnterRecoveryState(SignInReason.Blocked);
            return RefreshOutcome.Auth;
        }
        catch (NetworkException)
        {
            WriteSnapshotError(SnapshotContract.ErrorNetwork);
            Fail("No connection — retrying…");
            return RefreshOutcome.Transient;
        }
        catch (HttpRequestException)
        {
            WriteSnapshotError(SnapshotContract.ErrorNetwork);
            Fail("No connection — retrying…");
            return RefreshOutcome.Transient;
        }
        catch (Exception ex)
        {
            Fail($"Error: {Truncate(ex.Message, 60)}");
            return RefreshOutcome.Other;
        }
        finally
        {
            _refreshing = false;
            // A rebuild's own fetch was turned away while this stale one was out
            // (single-flight): fetch now for the current fetcher. Demo mode fetches nothing.
            if (generation != _fetcherGeneration && !IsDemo && _fetcher is not null)
                _ = RefreshCoreAsync();
        }
    }

    /// <summary>After a post-rebuild first fetch comes back TRANSIENT, retry once
    /// after a short delay. Root cause: an account switch (and a post-sign-in
    /// reload) cold-starts <see cref="WebView2ApiClient"/> against the SHARED
    /// webview2-fetch profile, and its anti-bleed cookie wipe forces a fresh
    /// Cloudflare re-clearance that can race this very first fetch — the hidden
    /// page usually finishes clearing within a few seconds, well before the next
    /// 5-minute timer tick. One delayed retry closes that race. Never retries an
    /// <see cref="RefreshOutcome.Auth"/> outcome — SessionExpired/CloudflareBlocked
    /// already routed to the recovery card, which is the correct end state.
    /// Generation-guarded: if another switch/rebuild/sign-out lands during the
    /// delay, <paramref name="outcome"/>'s context is stale and the retry is
    /// skipped so it can't stomp on the newer state.</summary>
    private async Task RetryOnceIfTransientAsync(RefreshOutcome outcome)
    {
        if (outcome != RefreshOutcome.Transient)
            return;
        // The transient failure here is the post-rebuild settle race, not an
        // outage — fetch-debug proved the connection was fine while the generic
        // "No connection" text showed. Say what's actually happening for the
        // retry window; a failed retry falls back to the honest Fail() text.
        StatusText = "Reconnecting…";
        int generation = _fetcherGeneration;
        await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
        if (generation != _fetcherGeneration || _fetcher is null || _lastData is not null || Reason != SignInReason.None)
            return;
        await RefreshCoreAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void TogglePin()
    {
        Pinned = !Pinned;
        _settings.SavePinned(Pinned);
    }

    /// <summary>Toggle compact mode — collapse the card list to the busiest tier and
    /// signal the window to hide its toolbar + auto-size. Re-renders from the cached
    /// fetch, never a refetch (parity with widget._toggle_compact). In-memory only:
    /// resets to expanded each launch, matching Python.</summary>
    [RelayCommand]
    private void ToggleCompact()
    {
        IsCompact = !IsCompact;
        if (ShownData is { } shown)
            RenderCards(shown, DateTimeOffset.UtcNow);
        CompactChanged?.Invoke(IsCompact);
    }

    /// <summary>Toggle the widget's "Themes" swatch grid open/closed (the thin header
    /// click). Persistence rides the <see cref="OnIsThemesExpandedChanged"/> hook so the
    /// state survives relaunch.</summary>
    [RelayCommand]
    private void ToggleThemes() => IsThemesExpanded = !IsThemesExpanded;

    /// <summary>Cycle the sparkline style (Classic ↔ Horizon) across every card with
    /// no refetch — AffectsRender re-renders each control, mirroring the theme-strip
    /// live switch. Persisted (a deliberate upgrade; Python resets to classic each
    /// launch).</summary>
    [RelayCommand]
    private void CycleSparklineStyle()
    {
        var styles = (SparklineStyle[])Enum.GetValues(typeof(SparklineStyle));
        int i = Array.IndexOf(styles, SparklineStyle);
        SparklineStyle = styles[(i + 1) % styles.Length];
        foreach (var card in Tiers)
            card.ApplyStyle(SparklineStyle);
        _settings.SaveSparklineStyle(SparklineStyle);
        Sounds.PlayToggle();
    }

    /// <summary>Focus affordance entry point — raises <see cref="FocusToggleRequested"/>
    /// so the window enters/exits focus mode (hourglass replaces the cards).</summary>
    [RelayCommand]
    private void ToggleFocus() => FocusToggleRequested?.Invoke();

    /// <summary>Game affordance entry point — raises <see cref="GameStartRequested"/>
    /// so the window shows the cooldown snake overlay.</summary>
    [RelayCommand]
    private void StartGame() => GameStartRequested?.Invoke();

    partial void OnIsThemesExpandedChanged(bool value) => _settings.SaveThemesExpanded(value);

    private void Fail(string message)
    {
        StatusText = message;
        ClearTray();
    }

    /// <summary>Drop the widget into a clean recovery state: show the reason card and
    /// wipe the now-stale signed-in chrome (tier cards, plan badge, account label,
    /// footer) so an expired/blocked session reads as fully signed out. The credential
    /// stays put so "Sign in again" refreshes the same account in place.</summary>
    private void EnterRecoveryState(SignInReason reason)
    {
        Reason = reason;
        _alertEngine.Reset();   // never alert off another context's baseline
        NotifyPromptCopy();
        StatusText = "";
        ClearSignedInChrome();
        ClearTray();
    }

    /// <summary>Wipe the signed-in chrome — tier cards, plan badge, account label,
    /// footer — so a no-account / expired / signed-out state never shows stale data.</summary>
    private void ClearSignedInChrome()
    {
        Tiers.Clear();
        _lastData = null;
        HasPlanBadge = false;
        PlanTooltip = "";
        AccountSwitcherText = "";
        AccountSwitcherClickable = false;
        FooterText = "";
    }

    private void OnTick()
    {
        // Day rollover: trigger an immediate ingest so yesterday's number
        // closes within ~30s of midnight (hot-day rule), not at the next
        // 5-minute fetch.
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (today != _lastTickDate)
        {
            _lastTickDate = today;
            _vault?.TriggerIngest();
        }

        // Publish usage (opt-in): the daily schedule check and the MCP
        // handoff both ride this tick. Fire-and-forget, single-flight inside
        // the service, never awaited — and before the signed-out early return,
        // since the publisher reads local logs, not the claude.ai session.
        _publish?.Tick(DateTimeOffset.Now);
        _themeHandoff?.Tick();
        // A waiting Store update cannot install while this process holds its
        // files, so the widget is what tells the person and offers to quit.
        _storeUpdate?.Tick(DateTimeOffset.Now);

        if (ShownData is null)
            return;
        var now = DateTimeOffset.UtcNow;
        foreach (var vm in Tiers)
            vm.Tick(now);
        // Demo cards carry no real Claude Code burn.
        if (!IsDemo)
            _ = RefreshCcDerivedAsync();
    }

    /// <summary>ONE shared TokensSince walk feeding BOTH the per-tier badges
    /// and the footer's "CC +Nk" (the tick previously ran two synchronous
    /// walks on the UI thread). Off the UI thread via Task.Run; the await
    /// continuation returns to the dispatcher (DispatcherTimer ticks run on
    /// the UI thread), so the property/collection writes stay UI-safe.</summary>
    private async Task RefreshCcDerivedAsync()
    {
        if (_lastFetchAt is null || _ccScanRunning)
            return;
        _ccScanRunning = true;
        try
        {
            var anchor = _lastFetchAt.Value;
            Dictionary<string, long> byModel;
            try
            {
                byModel = await Task.Run(() => _ccReader.TokensSince(anchor)).ConfigureAwait(true);
            }
            catch
            {
                byModel = new Dictionary<string, long>();
            }
            // Demo mode came on during the scan: real burn never lands on sample cards.
            if (IsDemo)
                return;
            long total = 0;
            var byTier = new Dictionary<string, long>();
            foreach (var (model, tokens) in byModel)
            {
                total += tokens;
                var tier = CcLogReader.TierForModel(model);
                if (tier is not null)
                    byTier[tier] = byTier.GetValueOrDefault(tier) + tokens;
            }
            foreach (var vm in Tiers)
                vm.SetLocalDelta(byTier.GetValueOrDefault(vm.TierKey));
            _ccDeltaTotal = total;
            UpdateFooter();
        }
        finally
        {
            _ccScanRunning = false;
        }
    }

    private void RenderCards(JsonObject data, DateTimeOffset now)
    {
        var utilByKey = new Dictionary<string, double?>();
        foreach (var key in TierModel.EffectiveOrder)
            utilByKey[key] = Util(data, key);

        var hiddenKeys = new HashSet<string>(_hidden);
        hiddenKeys.UnionWith(_limitState.HiddenUntilReset.Keys);
        var active = TierModel.ActiveTiers(utilByKey, _savedOrder, hiddenKeys);
        if (IsCompact && active.Count > 0)
        {
            // Compact view: collapse to the single busiest tier (first-max, matching
            // Python `max(active, key=util)`). Downstream get-or-create then destroys
            // the rest and keeps the one complete card.
            string top = active[0];
            double best = utilByKey[active[0]] ?? 0;
            foreach (var k in active)
            {
                double u = utilByKey[k] ?? 0;
                if (u > best) { best = u; top = k; }
            }
            active = new List<string> { top };
        }
        var activeSet = new HashSet<string>(active);

        // Drop cards for tiers that no longer have data / were hidden.
        foreach (var stale in Tiers.Where(t => !activeSet.Contains(t.TierKey)).ToList())
            Tiers.Remove(stale);

        var existing = Tiers.ToDictionary(t => t.TierKey);
        int highest = -1;
        string? highestKey = null;

        for (int i = 0; i < active.Count; i++)
        {
            var key = active[i];
            if (!existing.TryGetValue(key, out var vm))
            {
                vm = new TierCardViewModel(key, _palette);
                Tiers.Insert(Math.Min(i, Tiers.Count), vm);
                existing[key] = vm;
            }
            else
            {
                int cur = Tiers.IndexOf(vm);
                if (cur != i)
                    Tiers.Move(cur, i);
            }

            var tier = data[key] as JsonObject;
            int util = (int)(utilByKey[key] ?? 0);
            string? resetsAt = tier?["resets_at"]?.GetValue<string>();
            int? used = GetInt(tier, "used");
            int? limit = GetInt(tier, "limit");
            var historyValues = IsDemo ? DemoMode.History(_demoAccount, key, now) : _history.Load(key);

            vm.Update(util, resetsAt, used, limit, historyValues, _palette, SparklineStyle, now);
            if (util > highest) { highest = util; highestKey = key; }
        }

        // Session and weekly read from the data whether or not their cards are hidden.
        _traySession = Util(data, TierModel.FiveHour);
        _trayWeekly = Util(data, TierModel.SevenDay);
        _trayHighest = highestKey is null ? null : (TierModel.Label(highestKey), highest);
        RaiseTray();
    }

    /// <summary>Map the fetch payload to alert snapshots — every known tier
    /// (canonical + dynamic scoped) with data participates, manually hidden or not (a
    /// cap is real whether or not its card is shown). Two opt-outs: a tier hidden
    /// until its reset, and a tier whose warnings the user muted, stay silent.</summary>
    private List<TierAlertSnapshot> BuildAlertSnapshots(JsonObject data)
    {
        var snaps = new List<TierAlertSnapshot>();
        foreach (var key in TierModel.EffectiveOrder)
        {
            var tier = data[key] as JsonObject;
            if (tier is null)
                continue;
            if (_limitState.HiddenUntilReset.ContainsKey(key) || _mutedTiers.Contains(key))
                continue;
            double? util = Util(data, key);
            snaps.Add(new TierAlertSnapshot(
                key,
                util is null ? null : (int)util.Value,
                GetInt(tier, "used"),
                GetInt(tier, "limit"),
                tier["resets_at"]?.GetValue<string>()));
        }
        return snaps;
    }

    /// <summary>Evaluate + deliver alerts for a fresh fetch. Never throws —
    /// alerting must not break the fetch loop.</summary>
    private void EvaluateAlerts(JsonObject data, DateTimeOffset now)
    {
        try
        {
            if (_alertService is null)
                return;
            var events = _alertEngine.Evaluate(BuildAlertSnapshots(data), _alertConfig, now);
            if (events.Count == 0)
                return;
            bool sound = _settings.LoadAlertSound();
            bool scopedChime = _settings.LoadAlertSoundScoped();
            bool snake = _settings.LoadAlertSnakeFull();
            foreach (var e in events)
            {
                // Model-scoped weekly tiers (sonnet/opus/fable + dynamic per-model
                // rows) are toast-only by default — a lone model's sub-limit
                // pinned at 100% isn't critical while the aggregate pools have
                // headroom. The "Chime for model-scoped weekly tiers" checkbox
                // restores sound for whoever wants it.
                bool audible = sound && (scopedChime || !TierModel.IsModelScoped(e.TierKey));
                _alertService.Deliver(e, TierModel.Label(e.TierKey), audible, snake);
            }
        }
        catch (Exception e)
        {
            try
            {
                File.AppendAllText(_paths.LogFile,
                    $"{DateTime.UtcNow:o} alert evaluate failed ({e.GetType().Name}){Environment.NewLine}");
            }
            catch { }
        }
    }

    private void UpdatePlanBadge()
    {
        var account = _client?.Account;
        var badge = account is null
            ? null
            : PlanLabel.Resolve(account.RateLimitTier, account.BillingType, account.Capabilities);
        if (badge is null)
        {
            HasPlanBadge = false;
            PlanBadgeName = "";
            PlanTooltip = "";
            _riffs = Array.Empty<string>();
            _riffTimer.Stop();
            return;
        }
        HasPlanBadge = true;
        PlanBadgeName = badge.Name;
        _riffs = badge.Riffs;
        _riffIndex = 0;
        PlanTooltip = _riffs.Count > 0 ? _riffs[0] : badge.Name;
        if (_riffs.Count > 1) _riffTimer.Start(); else _riffTimer.Stop();
    }

    private void RotateRiff()
    {
        if (_riffs.Count == 0) return;
        _riffIndex = (_riffIndex + 1) % _riffs.Count;
        PlanTooltip = _riffs[_riffIndex];
    }

    private void RefreshAccountLabel()
    {
        if (IsDemo)
        {
            AccountSwitcherText = $"⇆ {DemoMode.ChipText(_demoAccount)}";
            AccountSwitcherClickable = true;
            return;
        }
        var active = _accounts.GetActive();
        if (active is null)
        {
            AccountSwitcherText = "";
            AccountSwitcherClickable = false;
            return;
        }
        bool multiple = _accounts.ListAccounts().Count > 1;
        AccountSwitcherText = multiple ? $"⇆ {active}" : active;
        AccountSwitcherClickable = multiple;
    }

    private void UpdateFooter()
    {
        if (IsDemo) { FooterText = "Demo  ·  sample numbers"; return; }
        if (_lastFetchAt is null) { FooterText = ""; return; }
        string ts = _lastFetchAt.Value.ToString("h:mm tt", CultureInfo.InvariantCulture);
        string mode = IsCompact ? "Compact" : (Pinned ? "Pinned" : "Float");
        // The CC delta is computed by RefreshCcDerivedAsync's single shared
        // walk — the footer never does its own file IO anymore.
        string cc = _ccDeltaTotal > 0 ? $"  ·  CC +{TokenFormat.Compact(_ccDeltaTotal)}" : "";
        FooterText = $"Updated {ts}  ·  {mode}{cc}";
    }

    partial void OnPinnedChanged(bool value) => UpdateFooter();
    partial void OnIsCompactChanged(bool value) => UpdateFooter();

    // -- drag-reorder + hide (persisted) --------------------------------------

    /// <summary>Move a card to a new index (drag-reorder) and persist the order.</summary>
    public void MoveTier(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || oldIndex >= Tiers.Count) return;
        newIndex = Math.Clamp(newIndex, 0, Tiers.Count - 1);
        if (oldIndex == newIndex) return;
        Tiers.Move(oldIndex, newIndex);
        PersistOrder();
    }

    /// <summary>Hide a tier card and persist the hidden set.</summary>
    public void HideTier(string tierKey)
    {
        _hidden.Add(tierKey);
        _settings.SaveHiddenTiers(_hidden);
        var vm = Tiers.FirstOrDefault(t => t.TierKey == tierKey);
        if (vm is not null) Tiers.Remove(vm);
        RecomputeHighestForTray();
    }

    // -- limits that come and go (W2) ----------------------------------------

    /// <summary>Record first sightings and bring back any hidden-until-reset limit
    /// whose window renewed or refilled. Runs on each successful fetch, before cards render.</summary>
    private void ReconcileLimits(JsonObject data, DateTimeOffset now)
    {
        var present = new Dictionary<string, (string? ResetsAt, double? Util)>();
        foreach (var key in TierModel.EffectiveOrder)
        {
            if (data[key] is not JsonObject)
                continue;
            present[key] = (ResetsAtOf(data, key), Util(data, key));
        }
        var result = LimitLifetime.Reconcile(_limitState, present, now);
        bool changed = result.State.FirstSeen.Count != _limitState.FirstSeen.Count || result.Returned.Count > 0;
        _limitState = result.State;
        if (changed)
            _settings.SaveLimitState(_limitState);
    }

    private static string? ResetsAtOf(JsonObject data, string key)
    {
        try { return (data[key] as JsonObject)?["resets_at"]?.GetValue<string>(); }
        catch { return null; }
    }

    /// <summary>True when the tier is believed temporary, so "Hide until it resets" applies.</summary>
    public bool CanHideUntilReset(string tierKey)
    {
        // Never in demo mode: the sample resets would be saved as limit state.
        if (IsDemo || _lastData is null || _limitState.HiddenUntilReset.ContainsKey(tierKey))
            return false;
        DateTimeOffset? seen = _limitState.FirstSeen.TryGetValue(tierKey, out var s) ? s : null;
        return LimitLifetime.IsTemporary(tierKey, ResetsAtOf(_lastData, tierKey), DateTimeOffset.UtcNow, seen);
    }

    /// <summary>Hide a temporary limit until its window renews or refills.</summary>
    public void HideUntilReset(string tierKey)
    {
        if (IsDemo || _lastData is null)
            return;
        var next = LimitLifetime.Hide(
            _limitState, tierKey, ResetsAtOf(_lastData, tierKey), Util(_lastData, tierKey), DateTimeOffset.UtcNow);
        if (ReferenceEquals(next, _limitState))
            return;
        _limitState = next;
        _settings.SaveLimitState(_limitState);
        var vm = Tiers.FirstOrDefault(t => t.TierKey == tierKey);
        if (vm is not null) Tiers.Remove(vm);
        RecomputeHighestForTray();
    }

    /// <summary>Show a hidden limit again, whichever way it was hidden.</summary>
    public void ShowTier(string tierKey)
    {
        if (_hidden.Remove(tierKey))
            _settings.SaveHiddenTiers(_hidden);
        if (_limitState.HiddenUntilReset.ContainsKey(tierKey))
        {
            _limitState = LimitLifetime.Show(_limitState, tierKey);
            _settings.SaveLimitState(_limitState);
        }
        if (ShownData is { } shown)
            RenderCards(shown, DateTimeOffset.UtcNow);
    }

    /// <summary>Hidden limits (manual and until-reset), in canonical tier order.</summary>
    public IReadOnlyList<string> HiddenTierKeys =>
        TierModel.EffectiveOrder
            .Where(k => _hidden.Contains(k) || _limitState.HiddenUntilReset.ContainsKey(k))
            .ToList();

    public bool WarningsOn(string tierKey) => !_mutedTiers.Contains(tierKey);

    public void SetWarnings(string tierKey, bool on)
    {
        bool changed = on ? _mutedTiers.Remove(tierKey) : _mutedTiers.Add(tierKey);
        if (changed)
            _settings.SaveAlertMutedTiers(_mutedTiers);
    }

    private void PersistOrder()
    {
        var order = Tiers.Select(t => t.TierKey).ToList();
        _savedOrder.Clear();
        _savedOrder.AddRange(order);
        _settings.SaveTierOrder(order);
    }

    private void RecomputeHighestForTray()
    {
        TierCardViewModel? top = null;
        foreach (var vm in Tiers)
            if (top is null || (int)vm.Utilization > (int)top.Utilization) top = vm;
        _trayHighest = top is null ? null : (TierModel.Label(top.TierKey), (int)top.Utilization);
        RaiseTray();
    }

    // -- json helpers ---------------------------------------------------------

    private static double? Util(JsonObject data, string key)
    {
        if (data[key] is JsonObject t && t["utilization"] is JsonNode n)
        {
            try { return n.GetValue<double>(); }
            catch { return null; }
        }
        return null;
    }

    private static int? GetInt(JsonObject? t, string key)
    {
        if (t?[key] is JsonNode n)
        {
            try { return (int)n.GetValue<double>(); }
            catch { return null; }
        }
        return null;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    public void Dispose()
    {
        _refreshTimer.Stop();
        _tickTimer.Stop();
        _riffTimer.Stop();
        _statusNoticeTimer?.Stop();
        _themeHandoff?.Dispose();
        (_client as IDisposable)?.Dispose();
    }
}
