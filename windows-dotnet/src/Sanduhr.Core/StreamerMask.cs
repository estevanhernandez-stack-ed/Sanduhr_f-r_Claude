using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Sanduhr.Core;

/// <summary>
/// The one formatter every surface calls while streamer mode may be on (Settings v2, slice 7; spec, Streamer mode). Off (<see cref="Off"/>,
/// or built with <c>on: false</c>), every method returns its input as the page showed it before, and nothing is assigned. On, a name that is
/// not allowlisted becomes its codename, assigned the first time a surface asks and kept (<see cref="CodenameBook"/>); <see cref="Assigned"/>
/// tells the owner to save. Thread-safe. App-side Core: not on the MCP link list. It calls <see cref="SnapshotContract"/> and
/// <see cref="WatcherText"/>, which are linked, and changes neither.
/// </summary>
public sealed class StreamerMask
{
    // One lock for the book: StreamerService locks the book object itself when it saves, lists or changes the allowlist (preflight P10).
    private readonly object _gate;
    private readonly CodenameBook? _book;
    private readonly Regex[] _folderPatterns = [];
    private readonly Regex? _userNames;

    /// <param name="profileDir">The profile folder the Claude Code pages use (folders show as <c>~\…</c> under it).</param>
    /// <param name="userFolders">Folders <see cref="Paths(string?)"/> writes as <c>%USERPROFILE%</c>; the profile folder when null. The
    /// Windows profile folder is always added.</param>
    public StreamerMask(bool on, CodenameBook? book, string profileDir, IReadOnlyList<string>? userFolders = null)
    {
        On = on && book is not null;
        _book = book;
        _gate = (object?)book ?? new object();
        ProfileDir = profileDir;
        if (!On)
            return;
        var folders = (userFolders ?? [profileDir]).Append(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
            .Select(ClaudeCodeFolders.Norm).Where(f => f.Length > 3).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(f => f.Length).ToList();
        _folderPatterns = [.. folders.Select(FolderPattern)];
        // The bare Windows user name (and each user folder's leaf, which is the same person) when it stands as a path segment.
        var names = folders.Select(f => f.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries)[^1])
            .Append(Environment.UserName).Where(n => n.Length >= 2).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (names.Count > 0)
            _userNames = new Regex(@"(?<=[\\/])(?:" + string.Join("|", names.Select(Regex.Escape)) + @")(?![\p{L}\p{N}_-])(?!\.[\p{L}\p{N}_])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>Streamer mode off: nothing is masked and nothing is written.</summary>
    public static StreamerMask Off { get; } = new(false, null, "");

    public bool On { get; }
    public string ProfileDir { get; }

    /// <summary>A name got its codename just now (the book changed and needs saving).</summary>
    public event Action<CodenameCategory>? Assigned;

    /// <summary>The name, or its codename while on and not allowlisted. Blank stays blank.</summary>
    public string Name(CodenameCategory category, string? real)
    {
        if (string.IsNullOrWhiteSpace(real) || !On)
            return real ?? "";
        return Code(category, real.Trim()) ?? real;
    }

    /// <summary>Whether <see cref="Name"/> hides this name now (on, not blank, not allowlisted). Assigns nothing.</summary>
    public bool Hides(CodenameCategory category, string? real)
    {
        if (!On || string.IsNullOrWhiteSpace(real))
            return false;
        lock (_gate) return !_book!.IsShown(category, real.Trim());
    }

    /// <summary>A folder's key: its full path, without a trailing separator, lower-cased (invariant). Empty when the path can't be read.</summary>
    public static string FolderKey(string path) => ClaudeCodeFolders.Norm(path).ToLowerInvariant();

    /// <summary>A Claude Code folder as a card shows it: <c>~\.claude-work</c> off or allowlisted, else its codename alone.</summary>
    public string Folder(string path)
    {
        var shown = ClaudeCodeFolders.Display(path, ProfileDir);
        if (!On)
            return shown;
        var key = FolderKey(path);
        return key.Length == 0 ? Name(CodenameCategory.Folder, path) : Code(CodenameCategory.Folder, key) ?? shown;
    }

    /// <summary>A folder named by its leaf in the profile folder (the session archive, Folders to share, the MCP consent): the leaf off or
    /// allowlisted, else the same codename <see cref="Folder"/> gives.</summary>
    public string FolderName(string name)
    {
        if (!On || string.IsNullOrWhiteSpace(name))
            return name;
        return Code(CodenameCategory.Folder, FolderKey(Path.Combine(ProfileDir, name))) ?? name;
    }

    public bool HidesFolder(string path)
    {
        var key = FolderKey(path);
        return key.Length > 0 && Hides(CodenameCategory.Folder, key);
    }

    /// <summary>A plugin, marketplace, MCP server or pack id. <c>name@marketplace</c> masks each side, so "inline" (allowlisted) stays.</summary>
    public string AddOn(string? id)
    {
        if (!On || string.IsNullOrWhiteSpace(id))
            return id ?? "";
        int at = id.LastIndexOf('@');
        return at > 0 && at < id.Length - 1
            ? Name(CodenameCategory.AddOn, id[..at]) + "@" + Name(CodenameCategory.AddOn, id[(at + 1)..])
            : Name(CodenameCategory.AddOn, id);
    }

    // ---- Paths: free text that may hold a Windows path (error messages, status lines) ----

    // A path segment, and a path's tail: separators (one or many, either slash) and segments. A space joins two words of one directory name
    // only when a separator follows, so "D:\My Clients\x.json to E:\y" ends the first path at x.json. A quote ends a segment.
    private const string Seg = @"[^\s\\/""<>|*?'`]+";
    private const string Tail = @"(?:[\\/]+(?:" + Seg + "(?: " + Seg + @"(?=[\\/]))*)?)";
    private const string Edge = @"(?<![\p{L}\p{N}_])";
    private const string Punctuation = ".,;:!?)]}'\"`";

    private static readonly Regex AnyPath = new(
        @"(?<q>['""`])(?<qp>(?:\\\\\?\\)?(?:[A-Za-z]:[\\/]|\\\\(?!\?)|~[\\/])[^\r\n'""`]*?)\k<q>"
        + "|" + Edge + @"(?<p>(?:\\\\\?\\)?[A-Za-z]:" + Tail + "+"
        + @"|\\\\(?!\?)(?:" + Seg + "(?: " + Seg + @"(?=[\\/]))*)" + Tail + "*"
        + "|~" + Tail + "+)",
        RegexOptions.CultureInvariant);

    private static Regex FolderPattern(string folder)
    {
        bool unc = folder.StartsWith(@"\\", StringComparison.Ordinal);
        var core = (unc ? @"\\\\" : "") + string.Join(@"[\\/]+", folder.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape));
        return new Regex(Edge + @"(?:\\\\\?\\)?" + core + @"(?![\p{L}\p{N}_-])(?!\.[\p{L}\p{N}_])(?<tail>" + Tail + "*)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>
    /// Text that may hold a path (an exception message, a status line). While on, every Windows path in it is masked:
    /// <list type="bullet">
    /// <item>a user folder (the profile folder, the Windows profile folder, <c>C:\Users\&lt;anyone&gt;</c>) and <c>~\</c> become
    /// <c>%USERPROFILE%</c>, the rest of the path kept: <c>%USERPROFILE%\AppData\Roaming\Sanduhr\x.json</c>;</item>
    /// <item>any other drive path becomes <c>D:\…\leaf</c> and a UNC path <c>\\…\leaf</c>: where it lives is hidden, the file or folder it names stays;</item>
    /// <item>the bare Windows user name, standing as a path segment, becomes <c>%USERNAME%</c>.</item>
    /// </list>
    /// Quoted paths (with spaces), a path followed by punctuation, forward slashes, doubled slashes (JSON-escaped) and <c>\\?\</c> are
    /// recognised; the result uses single backslashes and masking it again changes nothing. A directory name with a space that sits in the
    /// leaf position of an unquoted path shows its first word; quoted, it is masked whole. Nothing else in the text changes.
    /// </summary>
    public string Paths(string? text)
    {
        if (!On || string.IsNullOrEmpty(text))
            return text ?? "";
        foreach (var pattern in _folderPatterns)
            text = pattern.Replace(text, m =>
            {
                var (core, trail) = SplitTrailing(m.Groups["tail"].Value);
                return "%USERPROFILE%" + Separators(core).TrimEnd('\\') + trail;
            });
        if (_userNames is not null)
            text = _userNames.Replace(text, "%USERNAME%");
        return AnyPath.Replace(text, m =>
        {
            if (m.Groups["qp"].Success)
                return m.Groups["q"].Value + MaskPath(m.Groups["qp"].Value) + m.Groups["q"].Value;
            var (core, trail) = SplitTrailing(m.Groups["p"].Value);
            return MaskPath(core) + trail;
        });
    }

    private static (string Core, string Trail) SplitTrailing(string s)
    {
        int end = s.Length;
        while (end > 0 && Punctuation.Contains(s[end - 1]))
            end--;
        return (s[..end], s[end..]);
    }

    private static string Separators(string s) => Regex.Replace(s, @"[\\/]+", "\\");

    /// <summary>One path, already cut from its sentence: see <see cref="Paths"/>.</summary>
    private static string MaskPath(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
            path = path[4..];
        const string hidden = "…";
        if (path.StartsWith('~'))
            return "%USERPROFILE%" + Separators(path[1..]).TrimEnd('\\');
        bool unc = path.StartsWith(@"\\", StringComparison.Ordinal);
        string root = unc ? "" : path[..2];   // "D:"
        var segments = path[2..].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (unc)
        {
            // The server and share name a machine and a folder; keep the leaf (UNC form: two backslashes, server, share, leaf). A masked one stays as it is.
            bool masked = segments.Length > 0 && segments[0] == hidden;
            return segments.Length >= (masked ? 2 : 3) ? @"\\" + hidden + "\\" + segments[^1] : @"\\" + hidden;
        }
        if (segments.Length == 0)
            return root + "\\";
        if (segments.Length >= 2 && string.Equals(segments[0], "Users", StringComparison.OrdinalIgnoreCase) && segments[1] != hidden)
            return "%USERPROFILE%" + (segments.Length > 2 ? "\\" + string.Join('\\', segments[2..]) : "");
        return segments.Length == 1 ? root + "\\" + hidden : root + "\\" + hidden + "\\" + segments[^1];
    }

    /// <summary>A publish endpoint: the host as a codename, the path hidden as "/…" (spec, Publish usage).</summary>
    public string Endpoint(string? url)
    {
        if (!On || string.IsNullOrWhiteSpace(url))
            return url ?? "";
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            return Name(CodenameCategory.Host, url);
        var rest = uri.PathAndQuery is "" or "/" ? "" : "/…";
        return $"{uri.Scheme}://{Name(CodenameCategory.Host, uri.Host)}{rest}";
    }

    // ---- HostsIn: the publish result (preflight P2) ----

    private static readonly Regex Rejected = new(@"Rejected \((\d{3})\):[\s\S]*", RegexOptions.CultureInvariant);

    // A host-shaped word, but not a file name inside a path (preceded by a backslash or a single slash) and not the tail of a longer word.
    private static readonly Regex HostShape = new(
        @"(?<!\\)(?<![^/]/)(?<![\w.%-])\b(?:[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?\.)+[A-Za-z]{2,}\b",
        RegexOptions.CultureInvariant);

    private static readonly Regex Ipv4 = new(@"(?<![\w.])\d{1,3}(?:\.\d{1,3}){3}(?![\w]|\.\d)", RegexOptions.CultureInvariant);

    /// <summary>A line that may name a host (a publish result). While on: the server's body after <c>Rejected (NNN):</c> is cut to
    /// <c>Rejected (NNN).</c> (it can say anything), paths are masked, the endpoint's own host and every other host-shaped word (a redirect's
    /// target included) and IPv4 address become their codenames.</summary>
    public string HostsIn(string? text, string? url)
    {
        if (!On || string.IsNullOrEmpty(text))
            return text ?? "";
        text = Rejected.Replace(text, "Rejected ($1).");
        text = Paths(text);
        if (Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
            text = Regex.Replace(text, @"(?<![\w.-])" + Regex.Escape(uri.Host) + @"(?![\w-]|\.\w)",
                _ => Name(CodenameCategory.Host, uri.Host.ToLowerInvariant()), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        text = HostShape.Replace(text, m => Name(CodenameCategory.Host, m.Value.ToLowerInvariant()));
        return Ipv4.Replace(text, m => Name(CodenameCategory.Host, m.Value));
    }

    /// <summary>The Desk's watcher cards: a CI runner's card names its runner by codename and keeps the job (spec, the Desk). Claude's own
    /// cards are its words, which no codename can map (slice 7 decision 11); only a path in them is masked (preflight P4).</summary>
    public IReadOnlyList<Watcher> Watchers(IReadOnlyList<Watcher> watchers)
    {
        if (!On)
            return watchers;
        return [.. watchers.Select(Mask)];

        Watcher Mask(Watcher w)
        {
            if (w.Source == WatcherSource.Runner && !string.IsNullOrWhiteSpace(w.Runner))
                return w with { Title = WatcherText.Clip($"{Name(CodenameCategory.Runner, w.Runner)} · {w.Job ?? w.Short}", WatcherLimits.Title) };
            var title = Paths(w.Title);
            var shortTitle = Paths(w.Short);
            var note = w.Note is null ? null : Paths(w.Note);
            return title == w.Title && shortTitle == w.Short && note == w.Note ? w : w with { Title = title, Short = shortTitle, Note = note };
        }
    }

    /// <summary>The name the weather piece draws in place of the place's own: the "Show it as" nickname when set (spec), else, while on,
    /// the city's codename; else empty (the piece draws the place's name).</summary>
    public string WeatherName(WeatherPlace? place, string? nickname)
    {
        if (!string.IsNullOrWhiteSpace(nickname))
            return nickname;
        return On && place is not null ? Name(CodenameCategory.City, place.Label) : "";
    }

    /// <summary>A copy of a publish record for Show a sample: folder and project names as codenames, this PC's name as
    /// <paramref name="thisPc"/>. The record itself is never changed (it is what would be sent).</summary>
    public JsonObject PublishSample(JsonObject payload, string thisPc)
    {
        var copy = (JsonObject)JsonNode.Parse(payload.ToJsonString())!;
        if (!On)
            return copy;
        if (copy["machine"] is not null)
            copy["machine"] = thisPc;
        if (copy["homes"] is JsonArray homes)
            for (int i = 0; i < homes.Count; i++)
                if (homes[i] is JsonValue v && v.TryGetValue(out string? home))
                    homes[i] = FolderName(home);
        if (copy["byProject"] is JsonArray projects)
            foreach (var p in projects.OfType<JsonObject>())
                if (p["name"] is JsonValue v && v.TryGetValue(out string? name))
                    p["name"] = Name(CodenameCategory.Project, name);
        return copy;
    }

    /// <summary>The codename, or null when the name is allowlisted. Assigns under the lock and announces outside it.</summary>
    private string? Code(CodenameCategory category, string key)
    {
        string code;
        bool fresh;
        lock (_gate)
        {
            if (_book!.IsShown(category, key))
                return null;
            fresh = !_book.TryGet(category, key, out code);
            if (fresh)
                code = _book.Assign(category, key);
        }
        if (fresh)
            Assigned?.Invoke(category);
        return code;
    }
}
