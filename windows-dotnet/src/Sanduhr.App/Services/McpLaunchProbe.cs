using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Sanduhr.Core;

namespace Sanduhr.App.Services;

/// <summary>What starting the installed MCP launcher the way Claude Code does showed.</summary>
/// <param name="Ok">The launcher started the server and it answered <c>initialize</c> as Sanduhr.</param>
/// <param name="Version">The server's version from that answer, when it gave one.</param>
/// <param name="Reason">Why it is not OK, in words for the status line (no paths: streamer mode has nothing to mask).</param>
internal sealed record McpProbeResult(bool Ok, string? Version, string? Reason)
{
    public static McpProbeResult Answered(string? version) => new(true, version, null);
    public static McpProbeResult Failed(string reason) => new(false, null, reason);
}

/// <summary>
/// #144: a registration in <c>.claude.json</c> proves nothing. A command that does not exist fails at spawn, before any
/// handshake, so Claude Code lists the server neither as connected nor as failed, and a session reads that as "Sanduhr has
/// no MCP". After an install this starts the launcher exactly as Claude Code does (<c>cmd /c</c> on the .cmd, stdio piped),
/// sends one <c>initialize</c> and waits for the reply, so a launcher that can't start is reported the moment it is made.
/// The server is a local process with no network (Sanduhr.Mcp's own contract); it exits when its stdin closes, and is
/// killed if it doesn't.
/// </summary>
internal static class McpLaunchProbe
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Test seam: replaces the real check (unit tests of the install flow never start a process).</summary>
    internal static Func<string, Task<McpProbeResult>>? Override { get; set; }

    private const string Initialize =
        "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\"," +
        "\"capabilities\":{},\"clientInfo\":{\"name\":\"sanduhr-install-check\",\"version\":\"1\"}}}";

    public static Task<McpProbeResult> CheckAsync(string launcherPath) =>
        Override is { } fake ? fake(launcherPath) : CheckAsync(launcherPath, DefaultTimeout);

    public static async Task<McpProbeResult> CheckAsync(string launcherPath, TimeSpan timeout)
    {
        if (!File.Exists(launcherPath))
            return McpProbeResult.Failed(SettingsNames.McpProbeNoLauncher);

        string cmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        var start = new ProcessStartInfo(cmd)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        // /d skips AutoRun, /s keeps the quoted path whole; the same shape Claude Code's spawn takes for a .cmd.
        start.ArgumentList.Add("/d");
        start.ArgumentList.Add("/s");
        start.ArgumentList.Add("/c");
        start.ArgumentList.Add(launcherPath);

        Process? process = null;
        try
        {
            // network: local (Sanduhr's own MCP server on this PC, through its launcher; the server has no network by contract)
            try { process = Process.Start(start); }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                return McpProbeResult.Failed(SettingsNames.McpProbeDidNotStart);
            }
            if (process is null)
                return McpProbeResult.Failed(SettingsNames.McpProbeDidNotStart);

            // Drained so a chatty server can't block on a full pipe; never shown (it can name a path, and streamer mode hides paths).
            _ = process.StandardError.ReadToEndAsync();
            await process.StandardInput.WriteLineAsync(Initialize).ConfigureAwait(false);
            await process.StandardInput.FlushAsync().ConfigureAwait(false);

            using var cts = new CancellationTokenSource(timeout);
            while (true)
            {
                string? line;
                try { line = await process.StandardOutput.ReadLineAsync(cts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return McpProbeResult.Failed(SettingsNames.McpProbeNoAnswer(timeout)); }
                if (line is null)
                {
                    // The output closed without an answer: the launcher's target is gone, or the server stopped at once.
                    // cmd's exit code doesn't say which (a missing quoted target exits 1, like a server that fails), so look:
                    // the launcher names its server on the quoted line the installer writes.
                    int? code = process.WaitForExit(2000) ? process.ExitCode : null;
                    return McpProbeResult.Failed(LauncherTarget(launcherPath) is { } target && !File.Exists(target)
                        ? SettingsNames.McpProbeTargetMissing
                        : SettingsNames.McpProbeExited(code));
                }
                if (Answer(line) is { } result)
                    return result;
                // Anything else on stdout before the answer (a notification) is not the answer: keep reading.
            }
        }
        catch (Exception e) when (e is IOException or InvalidOperationException)
        {
            return McpProbeResult.Failed(SettingsNames.McpProbeDidNotStart);
        }
        finally
        {
            if (process is not null)
            {
                try { process.StandardInput.Close(); } catch (Exception e) when (e is IOException or InvalidOperationException) { }
                try
                {
                    if (!process.WaitForExit(2000))
                        process.Kill(entireProcessTree: true);
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                process.Dispose();
            }
        }
    }

    /// <summary>The exe a launcher runs: the first quoted path on a line that starts with a quote, the form
    /// <see cref="McpIntegrationInstaller.WriteLauncher"/> writes (<c>"…\sanduhr-mcp.exe" %*</c>). Null when there is none.</summary>
    internal static string? LauncherTarget(string launcherPath)
    {
        try
        {
            foreach (var raw in File.ReadLines(launcherPath))
            {
                var line = raw.TrimStart();
                if (!line.StartsWith('"')) continue;
                int close = line.IndexOf('"', 1);
                if (close > 1) return line[1..close];
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return null;
    }

    /// <summary>The probe's verdict on one stdout line: the reply to id 1, or null when the line is something else.</summary>
    internal static McpProbeResult? Answer(string line)
    {
        JsonNode? frame;
        try { frame = JsonNode.Parse(line); }
        catch (System.Text.Json.JsonException) { return McpProbeResult.Failed(SettingsNames.McpProbeNotJson); }
        if (frame is not JsonObject obj || obj["id"]?.ToString() != "1")
            return null;
        if (obj["result"]?["serverInfo"] is not JsonObject info)
            return McpProbeResult.Failed(SettingsNames.McpProbeWrongAnswer);
        if ((string?)info["name"] != "sanduhr")
            return McpProbeResult.Failed(SettingsNames.McpProbeWrongAnswer);
        return McpProbeResult.Answered((string?)info["version"]);
    }
}
