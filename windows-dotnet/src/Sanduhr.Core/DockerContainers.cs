using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sanduhr.Core;

/// <summary>One container from Docker's list call. Only these fields are kept. <paramref name="Name"/> is
/// the label a card shows (the Compose service, which replicas share); <paramref name="Key"/> is the
/// container's own first name, unique per daemon and stable across a Compose recreate, and is what the
/// board tells runners apart by.</summary>
public sealed record DockerContainer(string Id, string Image, string Name, bool Running, string Key);

/// <summary>
/// The answer to <c>GET</c> on the container list (spec ci-watchers-3.9 sections 2 and 3). The list call
/// carries no environment (verified on Neb, 2026-10-23); Sanduhr reads id, image, names, state and the
/// Compose service label from it and nothing else. A runner is a running container whose image's own
/// name (no registry, owner, tag or digest) contains one of <see cref="RunnerImageWords"/>. Pure.
/// </summary>
public static partial class DockerContainers
{
    public const int MaxBytes = 4 * 1024 * 1024;

    /// <summary>The runner's name on a card at most this long.</summary>
    public const int NameLength = 40;

    public static readonly IReadOnlyList<string> RunnerImageWords = ["actions-runner", "github-runner", "gh-runner"];

    public static IReadOnlyList<DockerContainer> Parse(ReadOnlySpan<byte> json)
    {
        if (json.Length > MaxBytes) return [];
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json.ToArray()); }
        catch (JsonException) { return []; }
        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];
            var list = new List<DockerContainer>();
            foreach (var c in doc.RootElement.EnumerateArray())
            {
                if (c.ValueKind != JsonValueKind.Object) continue;
                if (Str(c, "Id") is not { } id || !IdPattern().IsMatch(id)) continue;
                var image = Str(c, "Image") ?? "";
                var service = c.TryGetProperty("Labels", out var labels) && labels.ValueKind == JsonValueKind.Object
                    ? Str(labels, "com.docker.compose.service")
                    : null;
                var first = c.TryGetProperty("Names", out var names) && names.ValueKind == JsonValueKind.Array
                    ? names.EnumerateArray().Select(n => n.ValueKind == JsonValueKind.String ? n.GetString() : null).FirstOrDefault()
                    : null;
                var own = first?.TrimStart('/');
                var name = WatcherLimits.Line(service ?? own, NameLength) ?? id[..12];
                var key = string.IsNullOrWhiteSpace(own) ? id : own;
                list.Add(new DockerContainer(id, image, name, Str(c, "State") == "running", key));
            }
            return list;
        }
    }

    /// <summary>The running runners. When two share a label (scaled Compose replicas) both show their
    /// container name instead, so cards and the status line tell them apart.</summary>
    public static IReadOnlyList<DockerContainer> Runners(IReadOnlyList<DockerContainer> all)
    {
        var runners = all.Where(c => c.Running && IsRunnerImage(c.Image)).ToList();
        var shared = runners.GroupBy(c => c.Name, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        return shared.Count == 0
            ? runners
            : runners.Select(c => shared.Contains(c.Name)
                ? c with { Name = WatcherLimits.Line(c.Key, NameLength) ?? c.Name }
                : c).ToList();
    }

    public static bool IsRunnerImage(string image)
    {
        var s = image;
        var at = s.IndexOf('@');
        if (at >= 0) s = s[..at];
        var slash = s.LastIndexOf('/');
        if (slash >= 0) s = s[(slash + 1)..];
        var colon = s.IndexOf(':');
        if (colon >= 0) s = s[..colon];
        s = s.ToLowerInvariant();
        return s.Length > 0 && RunnerImageWords.Any(s.Contains);
    }

    private static string? Str(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    [GeneratedRegex("^[0-9a-f]{64}\\z", RegexOptions.CultureInvariant)]
    internal static partial Regex IdPattern();
}

public enum RunnerStatusKind { Off, Watching, NoRunners, DockerDown, DockerDenied, DockerRefused }

/// <summary>The one line under "Show my CI runners' jobs" (spec ci-watchers-3.9 section 5).</summary>
public sealed record RunnerStatus(RunnerStatusKind Kind, IReadOnlyList<string> Runners)
{
    public const int MaxText = 120;

    public static RunnerStatus Off { get; } = new(RunnerStatusKind.Off, []);

    public string Text => Kind switch
    {
        RunnerStatusKind.Watching => Clip(Runners.Count == 1
            ? $"Watching 1 runner: {Runners[0]}"
            : $"Watching {Runners.Count} runners: {string.Join(", ", Runners)}"),
        RunnerStatusKind.NoRunners => "No runner containers are running.",
        RunnerStatusKind.DockerDown => "Docker isn't running.",
        RunnerStatusKind.DockerDenied => "Docker refused the connection. Your Windows account needs to be in the docker-users group.",
        RunnerStatusKind.DockerRefused => "Docker answered but refused the request.",
        _ => "",
    };

    private static string Clip(string s) => s.Length <= MaxText ? s : s[..(MaxText - 1)] + "…";

    public bool Equals(RunnerStatus? other) => other is not null && Kind == other.Kind && Runners.SequenceEqual(other.Runners);

    public override int GetHashCode() => HashCode.Combine(Kind, Runners.Count);
}
