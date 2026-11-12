using System.IO;
using System.Text.Json.Nodes;
using Sanduhr.App.Services;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.App.Tests;

/// <summary>The Codex usage service over scratch folders: plain rollouts in a fake Codex home, no real AppData or profile.</summary>
public sealed class CodexUsageServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset AsOf = Now.AddMinutes(-10);
    private static readonly AlertConfig Config = new(Enabled: true, WarnPct: 80, UrgentPct: 90, ProjectionEnabled: false, ResetEnabled: false);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-codex-svc-" + Guid.NewGuid().ToString("N"));
    private readonly Paths _paths;
    private readonly string _home;
    private DateTimeOffset _clock = Now;

    public CodexUsageServiceTests()
    {
        _paths = new Paths(Path.Combine(_root, "appdata"), Path.Combine(_root, "home"), Path.Combine(_root, "local"));
        _home = Path.Combine(_root, "codex");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* temp */ }
    }

    private static string Event(DateTimeOffset at, double five, double week, DateTimeOffset? fiveResets = null, DateTimeOffset? weekResets = null)
    {
        long r5 = (fiveResets ?? Now.AddHours(3)).ToUnixTimeSeconds();
        long r7 = (weekResets ?? Now.AddDays(5)).ToUnixTimeSeconds();
        return "{\"timestamp\":\"" + at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'") + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"rate_limits\":{\"limit_id\":\"codex\","
            + "\"primary\":{\"used_percent\":" + five.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"window_minutes\":300,\"resets_at\":" + r5 + "},"
            + "\"secondary\":{\"used_percent\":" + week.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"window_minutes\":10080,\"resets_at\":" + r7 + "},\"plan_type\":\"plus\"}}}";
    }

    private string Rollout(params string[] lines)
    {
        var dir = Path.Combine(_home, "sessions", "2026", "11", "09");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "rollout-2026-11-09T11-00-00-test.jsonl");
        File.WriteAllText(path, string.Join("\n", lines) + "\n");
        return path;
    }

    private (CodexUsageService Svc, UsageHistory History) Make(bool homeInstalled = true, bool withSnapshot = true, Func<bool>? resetDone = null)
    {
        var accounts = new AccountStore(new AppWindowFixture.MemoryCredentials());
        var history = new UsageHistory(accounts, _paths);
        var reader = new CodexLimitsReader(() => homeInstalled ? new[] { _home } : Array.Empty<string>(), null);
        var snap = withSnapshot ? new SnapshotWriter(_paths.CodexSnapshotFile, null, "5.0.0", source: "codex") : null;
        return (new CodexUsageService(reader, history, snap, () => _clock, null, resetDone: resetDone ?? (() => false)), history);
    }

    [Fact]
    public void Not_installed_reports_off_and_deletes_the_codex_snapshot()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_paths.CodexSnapshotFile)!);
        File.WriteAllText(_paths.CodexSnapshotFile, "{}");
        var (svc, _) = Make(homeInstalled: false);
        svc.Poll();
        Assert.Equal(CodexReadStatus.NotInstalled, svc.Current.Status);
        Assert.False(File.Exists(_paths.CodexSnapshotFile));
    }

    [Fact]
    public void A_reading_writes_history_once_per_event_not_once_per_poll()
    {
        Rollout(Event(AsOf, 40, 10));
        var (svc, history) = Make();
        svc.Poll();
        svc.Poll();
        Assert.Single(history.Load("five_hour", CodexUsageService.CodexHistoryAccount));
        Assert.Single(history.Load("seven_day", CodexUsageService.CodexHistoryAccount));
    }

    [Fact]
    public void A_new_event_appends_a_second_point()
    {
        var path = Rollout(Event(AsOf, 40, 10));
        var (svc, history) = Make();
        svc.Poll();
        File.AppendAllText(path, Event(AsOf.AddMinutes(3), 45, 11) + "\n");
        svc.Poll();
        Assert.Equal(new[] { 40, 45 }, history.Load("five_hour", CodexUsageService.CodexHistoryAccount));
    }

    [Fact]
    public void A_restart_does_not_duplicate_the_point_already_kept()
    {
        Rollout(Event(AsOf, 40, 10));
        var (first, history) = Make();
        first.Poll();
        var (second, _) = Make();   // a fresh service over the same history and rollout
        second.Poll();
        Assert.Single(history.Load("five_hour", CodexUsageService.CodexHistoryAccount));
    }

    [Fact]
    public void The_snapshot_carries_source_codex_and_no_account_ref()
    {
        Rollout(Event(AsOf, 40, 10));
        var (svc, _) = Make();
        svc.Poll();
        var root = JsonNode.Parse(File.ReadAllText(_paths.CodexSnapshotFile))!.AsObject();
        Assert.Equal("codex", root["source"]!.GetValue<string>());
        Assert.False(root.ContainsKey("account_ref"));
        Assert.Equal("plus", root["plan"]!.GetValue<string>());
        Assert.StartsWith(AsOf.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss"), root["captured_at"]!.GetValue<string>());
        var tiers = root["tiers"]!.AsArray().Select(t => t!.AsObject()).ToDictionary(t => t["key"]!.GetValue<string>());
        Assert.Equal(40, tiers["five_hour"]["utilization"]!.GetValue<int>());
        Assert.Equal(10, tiers["seven_day"]["utilization"]!.GetValue<int>());
    }

    [Fact]
    public void No_snapshot_writer_means_no_snapshot_file()
    {
        Rollout(Event(AsOf, 40, 10));
        var (svc, _) = Make(withSnapshot: false);
        svc.Poll();
        Assert.Equal(CodexReadStatus.Ok, svc.Current.Status);
        Assert.False(File.Exists(_paths.CodexSnapshotFile));
    }

    [Fact]
    public void Stale_limits_raise_no_alert()
    {
        Rollout(Event(AsOf, 99, 10));
        _clock = AsOf.AddMinutes(31);
        var (svc, _) = Make();
        svc.Poll();
        Assert.False(svc.Current.Fresh);
        Assert.Empty(svc.EvaluateAlerts(Config, true));
    }

    [Fact]
    public void Fresh_limits_over_the_warn_threshold_alert_and_claude_state_is_separate()
    {
        Rollout(Event(AsOf, 85, 10));
        _clock = AsOf.AddMinutes(1);
        var (svc, _) = Make();
        svc.Poll();
        var events = svc.EvaluateAlerts(Config, true);
        var warn = Assert.Single(events);
        Assert.Equal(AlertKind.Warn, warn.Kind);
        Assert.Equal("five_hour", warn.TierKey);

        // Claude's engine, keyed by the same tier name, is a different instance and still fires its own Warn.
        var claude = new AlertEngine();
        var claudeEvents = claude.Evaluate([new TierAlertSnapshot("five_hour", 85, null, null, null)], Config, _clock);
        Assert.Equal(AlertKind.Warn, Assert.Single(claudeEvents).Kind);
    }

    [Fact]
    public void Passed_resets_read_zero_and_do_not_alert_on_the_old_percent()
    {
        Rollout(Event(AsOf, 99, 99, fiveResets: Now.AddMinutes(-5), weekResets: Now.AddMinutes(-5)));
        var (svc, _) = Make();
        svc.Poll();
        Assert.Equal(0, svc.Current.Tiers["five_hour"]!["utilization"]!.GetValue<double>());
        Assert.True(svc.Current.Fresh);
        Assert.DoesNotContain(svc.EvaluateAlerts(Config, true), e => e.Kind is AlertKind.Warn or AlertKind.Urgent or AlertKind.Full);
    }

    [Fact]
    public void A_rebuilt_service_with_the_same_engine_does_not_toast_a_tier_again()
    {
        Rollout(Event(AsOf, five: 85, week: 10));
        var engine = new AlertEngine();
        var accounts = new AccountStore(new AppWindowFixture.MemoryCredentials());
        CodexUsageService Build() => new(new CodexLimitsReader(() => new[] { _home }, null), new UsageHistory(accounts, _paths), null, () => _clock, null, engine);
        var first = Build();
        first.Poll();
        Assert.Contains(first.EvaluateAlerts(Config, true), e => e.Kind == AlertKind.Warn);
        first.Dispose();
        var second = Build();
        second.Poll();
        Assert.DoesNotContain(second.EvaluateAlerts(Config, true), e => e.Kind == AlertKind.Warn);
        // Without the carried engine the same reading toasts again.
        var fresh = new CodexUsageService(new CodexLimitsReader(() => new[] { _home }, null), new UsageHistory(accounts, _paths), null, () => _clock, null);
        fresh.Poll();
        Assert.Contains(fresh.EvaluateAlerts(Config, true), e => e.Kind == AlertKind.Warn);
    }

    [Fact]
    public void Alerts_off_for_codex_raise_nothing()
    {
        Rollout(Event(AsOf, 99, 99));
        var (svc, _) = Make();
        svc.Poll();
        Assert.True(svc.Current.Fresh);
        Assert.Empty(svc.EvaluateAlerts(Config, alertsOn: false));
    }

    [Fact]
    public void Changed_fires_only_on_a_real_change()
    {
        Rollout(Event(AsOf, 40, 10));
        var (svc, _) = Make();
        int raised = 0;
        svc.Changed += _ => raised++;
        svc.Poll();
        svc.Poll();
        Assert.Equal(1, raised);
    }

    [Fact]
    public void The_first_not_installed_poll_is_a_change_from_the_startup_state()
    {
        // CodexState.Off is itself NotInstalled with nothing in it, so a field-by-field compare
        // would call the first real NotInstalled "no change" and the widget would never learn it.
        var (svc, _) = Make(homeInstalled: false);
        var states = new List<CodexState>();
        svc.Changed += s => states.Add(s);
        svc.Poll();
        svc.Poll();
        var only = Assert.Single(states);
        Assert.Equal(CodexReadStatus.NotInstalled, only.Status);
        Assert.False(ReferenceEquals(only, CodexState.Off));
    }

    [Fact]
    public void A_codex_history_never_inherits_the_legacy_claude_history_file()
    {
        Directory.CreateDirectory(_paths.AppDataDir);
        File.WriteAllText(_paths.HistoryFile, "{\"five_hour\":[{\"t\":\"2026-11-01T00:00:00+00:00\",\"v\":7}]}");
        Rollout(Event(AsOf, 40, 10));
        var (svc, history) = Make();
        svc.Poll();
        Assert.True(File.Exists(_paths.HistoryFile));
        Assert.Equal(new[] { 40 }, history.Load("five_hour", CodexUsageService.CodexHistoryAccount));
    }

    [Fact]
    public void An_idle_codex_alerts_on_no_tier_whose_reset_has_not_passed()
    {
        // Idle 6 h: the five-hour reset passed (reads 0), the seven-day window is still the old 85%.
        Rollout(Event(AsOf, 99, 85, fiveResets: AsOf.AddHours(5), weekResets: Now.AddDays(3)));
        _clock = AsOf.AddHours(6);
        var (svc, _) = Make();
        svc.Poll();
        Assert.True(svc.Current.Fresh);
        Assert.Equal(new[] { "five_hour" }, svc.Current.FreshTiers.OrderBy(x => x));
        Assert.Empty(svc.EvaluateAlerts(Config, true));
        Assert.Empty(svc.EvaluateAlerts(Config, true));   // and again: no event on a later pass either
    }

    [Fact]
    public void After_dispose_a_poll_does_no_io_and_raises_nothing()
    {
        Rollout(Event(AsOf, 40, 10));
        var (svc, history) = Make();
        int raised = 0;
        svc.Changed += _ => raised++;
        svc.Dispose();
        var state = svc.Poll();
        Assert.Equal(CodexReadStatus.NotInstalled, state.Status);
        Assert.False(File.Exists(_paths.CodexSnapshotFile));
        Assert.Empty(history.Load("five_hour", CodexUsageService.CodexHistoryAccount));
        Assert.False(File.Exists(_paths.HistoryFileFor(CodexUsageService.CodexHistoryAccount)));
        Assert.Equal(0, raised);
    }

    [Fact]
    public void A_callback_queued_before_stop_does_nothing_after_it()
    {
        var (svc, history) = Make();   // no rollout yet: Start's own first poll finds nothing and writes nothing
        svc.Start(new[] { _home });
        int live = svc.LiveGeneration;
        svc.Stop();
        Rollout(Event(AsOf, 40, 10));
        int raised = 0;
        svc.Changed += _ => raised++;
        svc.RunCallback(live);   // a callback queued before the stop
        Assert.NotEqual(live, svc.LiveGeneration);
        Assert.False(File.Exists(_paths.CodexSnapshotFile));
        Assert.False(File.Exists(_paths.HistoryFileFor(CodexUsageService.CodexHistoryAccount)));
        Assert.Empty(history.Load("five_hour", CodexUsageService.CodexHistoryAccount));
        Assert.Equal(0, raised);
        svc.Dispose();
    }

    [Fact]
    public void A_callback_with_the_live_generation_does_poll()
    {
        var (svc, history) = Make();
        svc.Start(new[] { _home });
        Rollout(Event(AsOf, 40, 10));
        svc.RunCallback(svc.LiveGeneration);   // synchronous; the lock serializes it with Start's timer poll
        Assert.Single(history.Load("five_hour", CodexUsageService.CodexHistoryAccount));
        Assert.True(File.Exists(_paths.CodexSnapshotFile));
        svc.Dispose();
    }

    [Fact]
    public void After_reset_a_callback_does_no_io()
    {
        bool done = false;
        var (svc, history) = Make(resetDone: () => done);
        svc.Start(new[] { _home });
        Rollout(Event(AsOf, 40, 10));
        done = true;
        svc.RunCallback(svc.LiveGeneration);
        svc.Poll();
        Assert.Empty(history.Load("five_hour", CodexUsageService.CodexHistoryAccount));
        Assert.False(File.Exists(_paths.CodexSnapshotFile));
        svc.Dispose();
    }

    [Fact]
    public void History_skips_an_unchanged_value_inside_five_minutes_and_keeps_a_changed_one()
    {
        var path = Rollout(Event(AsOf, 40, 10));
        var (svc, history) = Make();
        svc.Poll();
        File.AppendAllText(path, Event(AsOf.AddMinutes(1), 40, 10) + "\n");
        svc.Poll();
        File.AppendAllText(path, Event(AsOf.AddMinutes(2), 40, 10) + "\n");
        svc.Poll();
        Assert.Single(history.Load("five_hour", CodexUsageService.CodexHistoryAccount));
        File.AppendAllText(path, Event(AsOf.AddMinutes(3), 41, 10) + "\n");
        svc.Poll();
        Assert.Equal(new[] { 40, 41 }, history.Load("five_hour", CodexUsageService.CodexHistoryAccount));
        Assert.Single(history.Load("seven_day", CodexUsageService.CodexHistoryAccount));
        File.AppendAllText(path, Event(AsOf.AddMinutes(9), 41, 10) + "\n");
        svc.Poll();
        Assert.Equal(new[] { 40, 41, 41 }, history.Load("five_hour", CodexUsageService.CodexHistoryAccount));
    }
}
