using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
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

    private readonly List<string> _savedOrder;
    private readonly HashSet<string> _hidden;
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

    /// <summary>Visibility of the recovery card — any non-None reason shows it.</summary>
    public bool ShowSignInPrompt => Reason != SignInReason.None;
    /// <summary>Card headline for the current reason (empty when hidden).</summary>
    public string PromptHeadline => Reason == SignInReason.None ? "" : SignInPromptCopy.For(Reason).Headline;
    /// <summary>Card subtitle for the current reason (empty when hidden).</summary>
    public string PromptSubtitle => Reason == SignInReason.None ? "" : SignInPromptCopy.For(Reason).Subtitle;
    /// <summary>Primary-button label for the current reason (empty when hidden).</summary>
    public string PromptPrimaryLabel => Reason == SignInReason.None ? "" : SignInPromptCopy.For(Reason).PrimaryLabel;

    partial void OnReasonChanged(SignInReason value)
    {
        OnPropertyChanged(nameof(ShowSignInPrompt));
        OnPropertyChanged(nameof(PromptHeadline));
        OnPropertyChanged(nameof(PromptSubtitle));
        OnPropertyChanged(nameof(PromptPrimaryLabel));
    }

    /// <summary>Highest active-tier % for the tray glyph; -1 when no data.</summary>
    public event Action<int>? TrayPercentChanged;

    /// <summary>Raised by the "Sign in to Claude" command — App opens the embedded
    /// WebView2 <c>SignInWindow</c> flow, then calls <see cref="ReloadAfterSignInAsync"/>.</summary>
    public event Func<Task>? SignInRequested;

    /// <summary>Raised by the recovery card when the ACTIVE account needs re-auth
    /// (Expired/Blocked) — App routes this to <c>SignInCoordinator.ReauthenticateActiveAsync</c>,
    /// which refreshes the existing account in place rather than adding a new one.</summary>
    public event Func<Task>? ReauthRequested;

    /// <summary>Raised by the "Paste a key instead" command — App opens the manual
    /// sessionKey fallback modal.</summary>
    public event Func<Task>? PasteKeyRequested;

    /// <summary>Raised by the "Settings…" command (title-bar chip, widget right-click,
    /// tray) — App opens the <c>SettingsWindow</c> on the Accounts tab.</summary>
    public event Func<Task>? SettingsRequested;

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

    /// <summary>The saved focus-timer duration (settings.json "focus_mode_duration").</summary>
    public int LoadFocusDuration() => _settings.LoadFocusDuration();

    /// <summary>Persist the focus-timer duration chosen for a session.</summary>
    public void SaveFocusDuration(int minutes) => _settings.SaveFocusDuration(minutes);

    /// <summary>The saved cooldown-snake high score (settings.json "snake_high_score").</summary>
    public int LoadSnakeHighScore() => _settings.LoadSnakeHighScore();

    /// <summary>Persist a new cooldown-snake high score.</summary>
    public void SaveSnakeHighScore(int score) => _settings.SaveSnakeHighScore(score);

    public WidgetViewModel()
    {
        _paths = new Paths();
        _accounts = new AccountStore(new WindowsCredentialManager(AccountStore.Service));
        _credentials = new CredentialStore(_accounts, _paths);
        _history = new UsageHistory(_accounts, _paths);
        _settings = new SettingsStore(_paths);
        _ccReader = new CcLogReader();

        _pinned = _settings.LoadPinned();
        _isThemesExpanded = _settings.LoadThemesExpanded();
        _savedOrder = _settings.LoadTierOrder().ToList();
        _hidden = new HashSet<string>(_settings.LoadHiddenTiers());

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
        (_client as IDisposable)?.Dispose();
        _client = null;
        _fetcher = null;

        var creds = _credentials.Load();
        if (string.IsNullOrEmpty(creds.SessionKey))
        {
            // Empty state: the widget shows the "Sign in to Claude" prompt instead
            // of a status line, so clear StatusText to avoid the redundant message.
            Reason = SignInReason.FirstRun;
            StatusText = "";
            TrayPercentChanged?.Invoke(-1);
            return;
        }
        Reason = SignInReason.None;
        // WebView2-backed transport (item 3 pivot): a raw HttpClient can't pass
        // Cloudflare's fingerprint binding, but a real authenticated browser can.
        // Same (sessionKey, cfClearance) shape as ClaudeApiClient, same interface.
        _client = new WebView2ApiClient(creds.SessionKey, creds.CfClearance);
        _fetcher = new UsageFetcher(_client, _history);
    }

    /// <summary>Rebuild the fetcher against the now-active account, refresh the
    /// account label, and kick an immediate fetch — called after a successful
    /// embedded or manual sign-in so the widget starts tracking at once.</summary>
    public async Task ReloadAfterSignInAsync()
    {
        RebuildFetcher();
        RefreshAccountLabel();
        AccountsChanged?.Invoke();
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task SignIn()
    {
        if (SignInRequested is not null)
            await SignInRequested.Invoke();
    }

    /// <summary>The recovery card's primary button. FirstRun → add-account sign-in;
    /// Expired/Blocked → in-place re-auth of the active account.</summary>
    [RelayCommand]
    private async Task PrimaryAuth()
    {
        if (Reason is SignInReason.Expired or SignInReason.Blocked)
        {
            if (ReauthRequested is not null)
                await ReauthRequested.Invoke();
        }
        else
        {
            if (SignInRequested is not null)
                await SignInRequested.Invoke();
        }
    }

    [RelayCommand]
    private async Task PasteKey()
    {
        if (PasteKeyRequested is not null)
            await PasteKeyRequested.Invoke();
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
    /// cleared first so the old account's numbers never linger during the switch.
    /// </summary>
    [RelayCommand]
    private async Task SwitchAccount(string? label)
    {
        if (string.IsNullOrEmpty(label) || label == _accounts.GetActive())
            return;

        _accounts.SetActive(label);
        Tiers.Clear();
        _lastData = null;
        StatusText = "Switching account…";
        TrayPercentChanged?.Invoke(-1);

        RebuildFetcher();
        RefreshAccountLabel();
        AccountsChanged?.Invoke();
        await RefreshAsync();
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
    /// pointer follows the rename inside <see cref="AccountStore"/>, so the fetcher
    /// keeps the same session — only the displayed label refreshes.</summary>
    public void RenameAccount(string oldLabel, string newLabel)
    {
        _accounts.RenameAccount(oldLabel, newLabel);
        RefreshAccountLabel();
        AccountsChanged?.Invoke();
    }

    /// <summary>
    /// Sign out (remove) an account: wipe its per-account history file, drop its
    /// Credential-Manager slots, and — when it was the active one —
    /// <see cref="AccountStore.RemoveAccount"/> advances the active pointer to the
    /// first remaining account (or none). When the active account changed we rebuild
    /// the fetcher (anti-bleed cookie wipe runs again) and refetch; signing out the
    /// last account drops the widget into its "Sign in to Claude" empty state.
    /// </summary>
    public async Task SignOutAccountAsync(string label)
    {
        bool wasActive = _accounts.GetActive() == label;
        // Wipe history BEFORE removal so the file is targeted by an explicit label
        // (parity with the Python remove flow).
        _history.ClearAll(label);
        _accounts.RemoveAccount(label);

        if (wasActive)
        {
            Tiers.Clear();
            _lastData = null;
            RebuildFetcher();
            await RefreshAsync();
        }
        RefreshAccountLabel();
        AccountsChanged?.Invoke();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_fetcher is null || _refreshing)
            return;
        _refreshing = true;
        try
        {
            if (Tiers.Count == 0)
                StatusText = "Refreshing…";
            var data = await _fetcher.FetchAsync().ConfigureAwait(true);
            _lastData = data;
            _lastFetchAt = DateTimeOffset.Now;
            RenderCards(data, DateTimeOffset.UtcNow);
            // Fresh fetch re-anchors the Local CC delta window: badges reset to ~0
            // here, then the 30s tick grows them as CC events stream in between fetches.
            RefreshCcDelta();
            UpdatePlanBadge();
            StatusText = "";
            Reason = SignInReason.None;
            UpdateFooter();
        }
        catch (SessionExpiredException)
        {
            // Surface the actionable recovery card (one-click in-place re-auth) instead
            // of a dead status string. The stored key is non-empty but rejected, so
            // RebuildFetcher's empty-key gate never fires — set the reason here.
            Reason = SignInReason.Expired;
            StatusText = "";
            TrayPercentChanged?.Invoke(-1);
        }
        catch (CloudflareBlockedException)
        {
            // Cloudflare challenge — re-auth re-captures a fresh cf_clearance silently,
            // so point at the same recovery card rather than the manual cf_clearance box.
            Reason = SignInReason.Blocked;
            StatusText = "";
            TrayPercentChanged?.Invoke(-1);
        }
        catch (NetworkException)
        {
            Fail("No connection — retrying…");
        }
        catch (HttpRequestException)
        {
            Fail("No connection — retrying…");
        }
        catch (Exception ex)
        {
            Fail($"Error: {Truncate(ex.Message, 60)}");
        }
        finally
        {
            _refreshing = false;
        }
    }

    [RelayCommand]
    private void TogglePin()
    {
        Pinned = !Pinned;
        _settings.SavePinned(Pinned);
    }

    /// <summary>Toggle the widget's "Themes" swatch grid open/closed (the thin header
    /// click). Persistence rides the <see cref="OnIsThemesExpandedChanged"/> hook so the
    /// state survives relaunch.</summary>
    [RelayCommand]
    private void ToggleThemes() => IsThemesExpanded = !IsThemesExpanded;

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
        TrayPercentChanged?.Invoke(-1);
    }

    private void OnTick()
    {
        if (_lastData is null)
            return;
        var now = DateTimeOffset.UtcNow;
        foreach (var vm in Tiers)
            vm.Tick(now);
        RefreshCcDelta();
        UpdateFooter();
    }

    /// <summary>
    /// Recompute the per-tier Local CC token-burn deltas since <c>_lastFetchAt</c>
    /// and push them into the card badges. Ports <c>widget._refresh_cc_delta</c>:
    /// no-op before the first fetch; the CC-log model→tier mapping lives in
    /// <see cref="CcLogReader.TokensSinceByTier"/>; tiers with no local activity get
    /// zero (badge hidden). Defensive — log-file IO never takes down the widget.
    /// </summary>
    private void RefreshCcDelta()
    {
        if (_lastFetchAt is null)
            return;
        Dictionary<string, long> byTier;
        try
        {
            byTier = _ccReader.TokensSinceByTier(_lastFetchAt.Value);
        }
        catch
        {
            byTier = new Dictionary<string, long>();
        }
        foreach (var vm in Tiers)
            vm.SetLocalDelta(byTier.GetValueOrDefault(vm.TierKey));
    }

    private void RenderCards(JsonObject data, DateTimeOffset now)
    {
        var utilByKey = new Dictionary<string, double?>();
        foreach (var key in TierModel.CanonicalOrder)
            utilByKey[key] = Util(data, key);

        var active = TierModel.ActiveTiers(utilByKey, _savedOrder, _hidden);
        var activeSet = new HashSet<string>(active);

        // Drop cards for tiers that no longer have data / were hidden.
        foreach (var stale in Tiers.Where(t => !activeSet.Contains(t.TierKey)).ToList())
            Tiers.Remove(stale);

        var existing = Tiers.ToDictionary(t => t.TierKey);
        int highest = -1;

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
            var historyValues = _history.Load(key);

            vm.Update(util, resetsAt, used, limit, historyValues, _palette, now);
            if (util > highest) highest = util;
        }

        TrayPercentChanged?.Invoke(highest);
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
        if (_lastFetchAt is null) { FooterText = ""; return; }
        string ts = _lastFetchAt.Value.ToString("h:mm tt", CultureInfo.InvariantCulture);
        string mode = Pinned ? "Pinned" : "Float";
        // Aggregate Local CC burn since the anchor, appended as "· CC +Nk" when
        // there's been activity — parity with widget._update_footer's _cc_delta_lbl.
        string cc = "";
        try
        {
            long total = _ccReader.TokensSince(_lastFetchAt.Value).Values.Sum();
            if (total > 0)
                cc = $"  ·  CC +{TokenFormat.Compact(total)}";
        }
        catch
        {
            // Log-file IO must never break the footer.
        }
        FooterText = $"Updated {ts}  ·  {mode}{cc}";
    }

    partial void OnPinnedChanged(bool value) => UpdateFooter();

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

    private void PersistOrder()
    {
        var order = Tiers.Select(t => t.TierKey).ToList();
        _savedOrder.Clear();
        _savedOrder.AddRange(order);
        _settings.SaveTierOrder(order);
    }

    private void RecomputeHighestForTray()
    {
        int highest = -1;
        foreach (var vm in Tiers)
            if ((int)vm.Utilization > highest) highest = (int)vm.Utilization;
        TrayPercentChanged?.Invoke(highest);
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
        (_client as IDisposable)?.Dispose();
    }
}
