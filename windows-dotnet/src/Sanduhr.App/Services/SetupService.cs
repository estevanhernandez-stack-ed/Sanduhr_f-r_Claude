using System.IO;
using Sanduhr.App.Desk;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Services;

/// <summary>What setup changes, as a seam for tests (setup spec, sections 2 and 4).</summary>
internal interface ISetupHost
{
    bool DrawersOn { get; }
    /// <summary>A rules plan was accepted once (<see cref="DrawerSettings.AutoTidyUnlocked"/>): ticking drawers reviews only before that.</summary>
    bool DrawersEverSorted { get; }
    int DrawerFiles { get; }
    bool AddOnsInstalled { get; }
    bool UsageOn { get; }
    /// <summary>Turns drawers off the way the Drawers page does (the count question, the review dismissed, the files returned).</summary>
    SetupDrawersOff TurnOffDrawers();
    /// <summary>Turns drawers on the way the Drawers page does.</summary>
    void TurnOnDrawers();
    /// <summary>Opens the rules' plan for review; null when it opened, otherwise the line to show.</summary>
    string? SortNow();
    void SetUsage(bool on);
    void ReloadDesk();
}

/// <summary>Turning drawers off: whether they are off now, and the line to show (null for a plain yes or a plain no).</summary>
internal sealed record SetupDrawersOff(bool Off, string? Line);

internal sealed record SetupResult(SetupChoices Now, IReadOnlyList<string> Lines, bool ReviewOpened);

/// <summary>Applies the setup screen's ticks one thing at a time (spec 2, Continue): drawers off first (it can be refused), then the
/// saved choices and the Desk, drawers on, usage, one Desk reload, and last the first drawers review.</summary>
internal sealed class SetupService(SettingsStore store, ISetupHost host)
{
    public SetupChoices Current()
    {
        var s = store.LoadSetup() ?? SetupChoices.FirstRun.ToSettings(null) with { Desktop = false };
        return SetupChoices.Of(s, host.DrawersOn, store.LoadDesk().Enabled);
    }

    public IReadOnlyList<string> Preview(SetupChoices after) =>
        SetupChange.Lines(Current(), after, host.DrawerFiles, host.AddOnsInstalled);

    public SetupResult Apply(SetupChoices after)
    {
        if (SetupChange.Blocked(after) is { } blocked) return new SetupResult(Current(), [blocked], false);
        var lines = new List<string>();

        // Drawers off first: it can be refused (a no to the count, no free name, a file in use), and then drawers stay ticked
        // while the rest of the change still applies.
        bool drawers = after.Drawers;
        bool turnedOff = false;
        if (!after.Drawers && host.DrawersOn)
        {
            var off = host.TurnOffDrawers();
            if (off.Line is { } line) lines.Add(line);
            drawers = !off.Off;
            turnedOff = off.Off;
        }

        var now = after with { Drawers = drawers };
        try
        {
            store.SaveSetup(now.ToSettings(store.LoadSetup()));
            var desk = store.LoadDesk();
            if (desk.Enabled != now.DeskRuns) store.SaveDesk(desk with { Enabled = now.DeskRuns });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            lines.Add(SetupCopy.SaveFailed);   // nothing past the save is touched
            if (turnedOff) host.ReloadDesk();   // the turn-off already happened: the Desk stops drawing the drawers
            return new SetupResult(Current(), lines, false);
        }

        if (host.UsageOn != now.ClaudeUsage) host.SetUsage(now.ClaudeUsage);
        bool turnedOn = now.Drawers && !host.DrawersOn;
        if (turnedOn)
        {
            host.TurnOnDrawers();
            lines.Add(DrawerReview.FirstOnLine);   // the Drawers page says this when it turns drawers on
        }
        host.ReloadDesk();

        // The first review is for drawers just turned on, and only while no rules plan was ever accepted.
        bool review = false;
        if (turnedOn && !host.DrawersEverSorted)
        {
            var said = host.SortNow();
            review = said is null;
            if (said is not null && said != DrawerReview.NothingToSort) lines.Add(said);
        }
        return new SetupResult(now, lines, review);
    }
}

/// <summary>The running app's half of setup: drawers on and off behave as the Drawers page does.</summary>
internal sealed class AppSetupHost(Func<DrawerService?> drawers, Func<DrawerReviewService?> review, WidgetViewModel widget,
    Func<DeskController?> desk, SettingsStore store, Func<string, bool> confirm) : ISetupHost
{
    public bool DrawersOn => drawers()?.Settings.Enabled ?? store.LoadDrawers().Enabled;
    public bool DrawersEverSorted => (drawers()?.Settings ?? store.LoadDrawers()).AutoTidyUnlocked;
    public int DrawerFiles => drawers()?.ReturnPlan()?.Moves.Count ?? 0;
    public bool AddOnsInstalled => store.LoadMcpEnabled() || store.LoadStatuslineEnabled();
    public bool UsageOn => widget.UsageOn;

    public SetupDrawersOff TurnOffDrawers()
    {
        if (drawers() is not { } d) return new SetupDrawersOff(false, SettingsNames.DrawersSortUnavailable);   // no service, no way to return the files: drawers stay on
        if (d.ReturnPlan() is not { } plan) return new SetupDrawersOff(false, DrawerService.NoFreeName);
        if (plan.Moves.Count > 0 && !confirm(DrawerReturn.Prompt(plan.Moves.Count))) return new SetupDrawersOff(false, null);
        if (review() is { Reviewing: true } r) r.Dismiss();   // a plan under review would move files into drawers that are going
        var run = d.TurnOff();
        return new SetupDrawersOff(!d.Settings.Enabled, Say(run));   // a drawer folder that had to stay is in the note; drawers are off either way
    }

    public void TurnOnDrawers()
    {
        if (drawers() is not { } d)
        {
            store.SaveDrawers(store.LoadDrawers() with { Enabled = true });
            return;
        }
        d.Save(d.Settings with { Enabled = true });
        d.ShowAll();
    }

    public string? SortNow() =>
        review() is { } r
            ? r.SortNow() is { } said ? StreamerMode.Mask.Paths(said) : null   // streamer: masked - a refused plan's line can carry a path; Mask.Paths hides the profile folder while on
            : SettingsNames.DrawersSortUnavailable;

    public void SetUsage(bool on) => widget.SetUsageOn(on);

    public void ReloadDesk() => desk()?.Reload();

    /// <summary>What a return said, as the Drawers page words it: the failure alone, else the result line (counts only while streamer mode
    /// is on) and what had to stay. Null when it had nothing to say.</summary>
    private static string? Say(DrawerReturnRun result)
    {
        var text = result.Failure ?? string.Join(" ", new[] { result.Run.Shown, result.KeptNote }.OfType<string>().Where(l => l.Length > 0));   // streamer: masked - DrawerRun.Shown cuts the file-naming line to counts while on
        return text.Length == 0 ? null : StreamerMode.Mask.Paths(text);   // streamer: masked - Mask.Paths hides the profile folder in any path the line holds
    }
}
