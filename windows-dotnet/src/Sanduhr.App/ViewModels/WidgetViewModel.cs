using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using System.Text.Json.Nodes;
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

    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _tickTimer;
    private readonly DispatcherTimer _riffTimer;

    private ThemePalette _palette = ThemePalette.Obsidian;
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

    [ObservableProperty] private string _statusText = "Connecting…";
    [ObservableProperty] private bool _pinned;

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

    /// <summary>True when there's no active account — drives the widget's
    /// "Sign in to Claude" empty-state prompt (item 6 first-run entry point).</summary>
    [ObservableProperty] private bool _showSignInPrompt;

    /// <summary>Highest active-tier % for the tray glyph; -1 when no data.</summary>
    public event Action<int>? TrayPercentChanged;

    /// <summary>Raised by the "Sign in to Claude" command — App opens the embedded
    /// WebView2 <c>SignInWindow</c> flow, then calls <see cref="ReloadAfterSignInAsync"/>.</summary>
    public event Func<Task>? SignInRequested;

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

    public ThemePalette Palette => _palette;

    /// <summary>Account labels in registry order — drives the quick-switch menus.</summary>
    public IReadOnlyList<string> ListAccounts() => _accounts.ListAccounts();

    /// <summary>The active account label, or null when signed out.</summary>
    public string? ActiveAccount => _accounts.GetActive();

    public WidgetViewModel()
    {
        _paths = new Paths();
        _accounts = new AccountStore(new WindowsCredentialManager(AccountStore.Service));
        _credentials = new CredentialStore(_accounts, _paths);
        _history = new UsageHistory(_accounts, _paths);
        _settings = new SettingsStore(_paths);

        _pinned = _settings.LoadPinned();
        _savedOrder = _settings.LoadTierOrder().ToList();
        _hidden = new HashSet<string>(_settings.LoadHiddenTiers());

        _refreshTimer = new DispatcherTimer { Interval = RefreshInterval };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _tickTimer = new DispatcherTimer { Interval = TickInterval };
        _tickTimer.Tick += (_, _) => OnTick();
        _riffTimer = new DispatcherTimer { Interval = RiffInterval };
        _riffTimer.Tick += (_, _) => RotateRiff();
    }

    /// <summary>Wire the active account, start the timers, and kick the first
    /// fetch. Called once after the window is shown.</summary>
    public async void Start()
    {
        RebuildFetcher();
        RefreshAccountLabel();
        _tickTimer.Start();
        _refreshTimer.Start();
        await RefreshAsync();
    }

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
            ShowSignInPrompt = true;
            StatusText = "";
            TrayPercentChanged?.Invoke(-1);
            return;
        }
        ShowSignInPrompt = false;
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
            UpdatePlanBadge();
            StatusText = "";
            UpdateFooter();
        }
        catch (SessionExpiredException)
        {
            Fail("Session expired — re-add your key.");
        }
        catch (CloudflareBlockedException)
        {
            Fail("Cloudflare — add cf_clearance.");
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
        UpdateFooter();
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
        FooterText = $"Updated {ts}  ·  {mode}";
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
