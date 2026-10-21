namespace Sanduhr.Core;

/// <summary>What <see cref="ExternalDirMigration"/> was given. All paths are plain strings so the App decides what Sanduhr knows.</summary>
/// <param name="MarkerPath">The one-shot marker (<c>.external-dir-migration</c> in AppData): <c>done</c>, or <c>attempt N</c>.</param>
/// <param name="Folders">Full Claude Code folder paths: discovered, added, and every receipt's folder.</param>
/// <param name="McpHomes">Home names (under the profile) or full paths, as <see cref="McpIntegrationInstaller.CandidateConfigPaths"/> takes them.</param>
/// <param name="LegacyStatuslineScript">The pre-3.5.0 <c>%APPDATA%\Sanduhr\bin\sanduhr-statusline.ps1</c>.</param>
/// <param name="LegacyMcpLauncher">The pre-3.5.0 <c>%APPDATA%\Sanduhr\bin\sanduhr-mcp.cmd</c>.</param>
/// <param name="McpSourceDir">The app's bundled server folder; null or missing means the MCP re-point is skipped and retried.</param>
public sealed record ExternalDirMigrationInputs(
    string MarkerPath,
    IReadOnlyList<string> Folders,
    IReadOnlyList<string> McpHomes,
    string LegacyStatuslineScript,
    string LegacyMcpLauncher,
    StatuslineInstaller StatuslineInstaller,
    McpIntegrationInstaller McpIntegrationInstaller,
    ClaudeCodeIntegrations ClaudeCodeIntegrations,
    string? McpSourceDir);

/// <param name="ShortCircuited">The marker said done (or the attempt cap), so nothing ran.</param>
/// <param name="Completed">Ran with no failure; the marker now says done.</param>
public sealed record ExternalDirMigrationResult(
    bool ShortCircuited,
    bool Completed,
    int StatuslineRepointed,
    int McpRepointed,
    int MetersRepointed,
    int HooksRepointed,
    int Failed);

/// <summary>
/// The one-time re-point of pre-3.5.0 registrations (spec Plan B, B.4): every statusline, MCP entry, meters mod entry and glow
/// hook that is still Sanduhr's and still names <c>%APPDATA%\Sanduhr</c> is moved to <c>%USERPROFILE%\.sanduhr</c> through
/// the existing engines (backup, compare-and-swap, receipts). An entry that is no longer Sanduhr's is left alone. A marker
/// makes it one-shot: <c>done</c>, or <c>attempt N</c> with a cap. Never throws; the log carries counts and exception type
/// names only, never a path.
/// </summary>
public sealed class ExternalDirMigration(Action<string>? log = null, int maxAttempts = 3)
{
    private const string Prefix = "external-dir-migration";

    public ExternalDirMigrationResult Run(ExternalDirMigrationInputs inputs)
    {
        try
        {
            int attempts = ReadMarker(inputs.MarkerPath, out bool done);
            if (done || attempts >= maxAttempts)
            {
                log?.Invoke($"{Prefix}: skipped ({(done ? "done" : "attempt cap")})");
                return new ExternalDirMigrationResult(true, done, 0, 0, 0, 0, 0);
            }

            var t = new Tally();
            var folders = inputs.Folders.Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            foreach (var f in folders)
                Step(() => inputs.StatuslineInstaller.RepointLegacy(f, inputs.LegacyStatuslineScript), ref t.Statusline, t);

            RunMcp(inputs, t);

            foreach (var f in folders)
                Step(() => inputs.ClaudeCodeIntegrations.RepointLegacy(IntegrationKind.Meters, f), ref t.Meters, t);
            foreach (var f in folders)
                Step(() => inputs.ClaudeCodeIntegrations.RepointLegacy(IntegrationKind.Hooks, f), ref t.Hooks, t);

            bool completed = t.Failed == 0;
            WriteMarker(inputs.MarkerPath, completed ? "done" : $"attempt {attempts + 1}");
            log?.Invoke($"{Prefix}: statusline {t.Statusline}, mcp {t.Mcp}, meters {t.Meters}, hooks {t.Hooks}, failed {t.Failed}, {(completed ? "done" : $"attempt {attempts + 1}")}");
            return new ExternalDirMigrationResult(false, completed, t.Statusline, t.Mcp, t.Meters, t.Hooks, t.Failed);
        }
        catch (Exception e)
        {
            log?.Invoke($"{Prefix}: aborted ({e.GetType().Name})");
            return new ExternalDirMigrationResult(false, false, 0, 0, 0, 0, 1);
        }
    }

    private sealed class Tally { public int Statusline, Mcp, Meters, Hooks, Failed; }

    private void Step(Func<RepointOutcome> step, ref int repointed, Tally t)
    {
        RepointOutcome outcome;
        try { outcome = step(); }
        catch (Exception e)
        {
            log?.Invoke($"{Prefix}: step threw ({e.GetType().Name})");
            outcome = RepointOutcome.Failed;
        }
        if (outcome == RepointOutcome.Repointed) repointed++;
        else if (outcome == RepointOutcome.Failed) t.Failed++;
    }

    private void RunMcp(ExternalDirMigrationInputs inputs, Tally t)
    {
        var files = new List<string>();
        foreach (var home in inputs.McpHomes.Where(h => !string.IsNullOrWhiteSpace(h)))
        {
            try
            {
                foreach (var p in inputs.McpIntegrationInstaller.CandidateConfigPaths(home))
                    if (!files.Any(x => string.Equals(Path.GetFullPath(x), Path.GetFullPath(p), StringComparison.OrdinalIgnoreCase)))
                        files.Add(p);
            }
            catch (Exception e)
            {
                log?.Invoke($"{Prefix}: mcp candidates failed ({e.GetType().Name})");
                t.Failed++;
            }
        }

        var holding = files.Where(f => HoldsLauncher(f, inputs.LegacyMcpLauncher)).ToList();
        if (holding.Count == 0) return;

        string? source = inputs.McpSourceDir;
        bool havePrepared = false;
        if (!string.IsNullOrEmpty(source) && Directory.Exists(source))
        {
            try { havePrepared = inputs.McpIntegrationInstaller.RefreshInstall(source) is not null; }
            catch (Exception e) { log?.Invoke($"{Prefix}: mcp refresh threw ({e.GetType().Name})"); }
        }
        if (!havePrepared)
        {
            log?.Invoke($"{Prefix}: mcp skipped (no server files to copy)");
            t.Failed++; // counts as not done, so a later run retries
            return;
        }
        foreach (var f in holding)
        {
            RepointOutcome outcome;
            try { outcome = inputs.McpIntegrationInstaller.RepointFile(f, inputs.LegacyMcpLauncher); }
            catch (Exception e)
            {
                log?.Invoke($"{Prefix}: mcp repoint threw ({e.GetType().Name})");
                outcome = RepointOutcome.Failed;
            }
            if (outcome == RepointOutcome.Repointed) t.Mcp++;
            else if (outcome == RepointOutcome.Failed) t.Failed++;
        }
    }

    /// <summary>A cheap look: does this file's text name the legacy launcher at all? RepointFile does the exact check.</summary>
    private static bool HoldsLauncher(string file, string launcher)
    {
        try
        {
            if (!File.Exists(file)) return false;
            var text = File.ReadAllText(file);
            // The launcher sits in JSON as an escaped string: backslashes doubled.
            return text.Contains(launcher, StringComparison.OrdinalIgnoreCase)
                || text.Contains(launcher.Replace("\\", "\\\\"), StringComparison.OrdinalIgnoreCase)
                || text.Contains(launcher.Replace("\\", "/"), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return true; // can't tell: let RepointFile decide (and fail visibly if it is ours and unreadable)
        }
    }

    private int ReadMarker(string path, out bool done)
    {
        done = false;
        try
        {
            if (!File.Exists(path)) return 0;
            var text = File.ReadAllText(path).Trim();
            if (text == "done") { done = true; return 0; }
            if (text.StartsWith("attempt ", StringComparison.Ordinal) && int.TryParse(text.AsSpan(8), out var n) && n >= 0) return n;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"{Prefix}: marker unreadable ({e.GetType().Name})");
        }
        return 0;
    }

    private void WriteMarker(string path, string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"{Prefix}: marker not written ({e.GetType().Name})");
        }
    }
}
