using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Sanduhr.Core;

/// <summary>The fields of a pack.json that passed validation.</summary>
public sealed record PackManifest(
    string Id,
    string Name,
    string Version,
    string Author,
    string License,
    string Kind,
    string SanduhrMin,
    string Description,
    IReadOnlyList<string> Files,
    string? Preview);

/// <summary>The verdict on one widget pack. <see cref="Errors"/> are precise sentences, one per finding, the same
/// sentences <c>sanduhr-widgets/tools/validate.py</c> prints. <see cref="Warnings"/> are the theme design rules; in
/// strict mode (the catalog's) they are errors instead and <see cref="Warnings"/> is empty.
/// <see cref="Manifest"/> is set only when the pack passed.</summary>
public sealed record PackValidation(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings, PackManifest? Manifest)
{
    public bool Ok => Errors.Count == 0;
}

/// <summary>
/// Sanduhr's port of the widget pack validator (<c>sanduhr-widgets</c> <c>tools/validate.py</c>, docs/FORMAT.md,
/// format v1 with sprite.json v1 and v2). One validator, three callers: the catalog's CI (the Python), the app on
/// every install, and the MCP server on every <c>propose_widget</c> before anything is written.
///
/// Pure: the input is the pack in memory (relative POSIX path to bytes, <c>pack.json</c> included) and the output
/// is a <see cref="PackValidation"/>. No disk, no network, no process, and it never throws on bad input. Whoever
/// turns a zip or a folder into the map refuses links and non-regular entries first (the map cannot hold one).
///
/// Messages match the Python byte for byte except where a rule runs through Core's <see cref="ThemeLint"/> (theme
/// schema and design rules), which keeps the app's own sentences. Where the Python accepts what FORMAT forbids, this
/// port follows FORMAT ("if this file and a tool disagree, the file wins"): a literal <c>null</c> where an object
/// belongs is an error, and anchors do not accept a trailing newline.
/// </summary>
public static class PackValidator
{
    // ---- format constants (FORMAT.md) --------------------------------------------------------------------------------
    public static readonly IReadOnlyList<string> Kinds = new[] { "theme", "palette", "effects", "font", "sprite" };

    /// <summary>SPDX id to the phrases of which one must appear in LICENSE (whitespace collapsed, lowercase), in
    /// FORMAT's order.</summary>
    private static readonly (string Id, string Phrase)[] Licenses =
    {
        ("MIT", "permission is hereby granted"),
        ("CC-BY-4.0", "attribution 4.0 international"),
        ("CC0-1.0", "cc0 1.0 universal"),
        ("Apache-2.0", "apache license"),
        ("OFL-1.1", "sil open font license"),
        ("BSD-3-Clause", "redistribution and use in source and binary forms"),
        ("ISC", "permission to use, copy, modify, and/or distribute"),
    };

    private static readonly string[] FontLicenses = { "OFL-1.1", "Apache-2.0" };
    private static readonly string[] AllowedExt = { ".json", ".png", ".ttf", ".otf" };
    private static readonly string[] BannedExt =
    {
        ".exe", ".dll", ".bat", ".cmd", ".ps1", ".sh", ".js", ".mjs", ".py", ".html", ".htm",
        ".svg", ".jar", ".msi", ".lnk", ".vbs", ".com", ".scr",
    };
    private static readonly string[] BannedSnippets = { "<script", "javascript:", "<iframe", "<!doctype html" };
    private static readonly (byte[] Sig, string What)[] ExecSignatures =
    {
        (new byte[] { (byte)'M', (byte)'Z' }, "a Windows executable"),
        (new byte[] { 0x7f, (byte)'E', (byte)'L', (byte)'F' }, "an ELF executable"),
        (new byte[] { (byte)'#', (byte)'!' }, "a script (shebang)"),
    };
    private static readonly string[] TrademarkSubstrings = { "claude", "anthropic" };
    private static readonly string[] TrademarkWords = { "opus", "sonnet", "haiku", "fable" };

    public const int MaxImageBytes = 2 * 1024 * 1024;
    public const int MaxImageSide = 4096;
    public const int MaxFontBytes = 5 * 1024 * 1024;
    public const int MaxPackBytes = 10 * 1024 * 1024;
    public const int MaxFiles = 64;
    public const int MaxLicenseBytes = 100 * 1024;

    // \z, not $: Python's $ also matches before a trailing newline, which FORMAT does not allow.
    private static readonly Regex IdRe = new(@"^[a-z][a-z0-9]*(-[a-z0-9]+)*\z", RegexOptions.CultureInvariant);
    private static readonly Regex SemverRe = new(@"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z", RegexOptions.CultureInvariant);
    private static readonly Regex HexRe = new(@"^#[0-9a-fA-F]{6}\z", RegexOptions.CultureInvariant);
    private static readonly Regex PathSegRe = new(@"^[A-Za-z0-9][A-Za-z0-9._-]*\z", RegexOptions.CultureInvariant);
    private static readonly Regex PresetNameRe = new(@"^[a-z0-9][a-z0-9-]{1,23}\z", RegexOptions.CultureInvariant);
    private static readonly Regex AnimNameRe = new(@"^[a-z][a-z0-9_]{1,23}\z", RegexOptions.CultureInvariant);
    private static readonly Regex ThemeFileRe = new(@"^themes/[a-z0-9]+(-[a-z0-9]+)*\.json\z", RegexOptions.CultureInvariant);
    private static readonly Regex CtrlRe = new(@"[\x00-\x1f\x7f]", RegexOptions.CultureInvariant);

    private static readonly string[] ManifestRequired =
        { "schema_version", "id", "name", "version", "author", "license", "kind", "sanduhr_min", "description", "files" };
    private static readonly string[] ManifestOptional = { "preview" };

    private static readonly string[] ThemeColorFields =
    {
        "bg", "glass", "glass_on_mica", "title_bg", "border", "text", "text_secondary", "text_dim", "text_muted",
        "accent", "bar_bg", "footer_bg", "pace_marker", "sparkline",
    };
    private static readonly string[] ThemeOptionalKeys =
    {
        "glass_alpha", "border_alpha", "border_tint", "accent_bloom", "inner_highlight", "card_corner_radius",
        "breath_period_ms", "opts_out_of_mica", "bg_grid", "monospace_font", "monospace_fallback",
        // theme match: the widget's font and the Desk look (Core's ThemeLint checks both).
        "font", "desk",
    };

    public static readonly IReadOnlyList<string> RequiredAnimations =
        new[] { "walk", "sit", "nap", "pop", "cheer", "worry", "focus", "sleep", "celebrate" };
    public static readonly IReadOnlyList<string> MoodKeys = new[] { "content", "focused", "worried", "exhausted", "celebrate" };
    public static readonly IReadOnlyList<string> TriggerKeys = new[] { "needs_you", "job_passed", "near_limit", "idle" };
    private static readonly string[] RoamEdges = { "bottom", "left", "right" };
    private static readonly string[] EffectTypes = { "glow", "sweep", "shimmer", "pulse" };

    internal static readonly string[] PixelKeys = { "palette", "frame_w", "frame_h", "frames" };
    internal const string PixelChars = ".0123456789abcdef";   // "." is transparent, the rest index the palette
    public const int PixelMaxColors = 16;
    public const int PixelFrameMin = 8;
    public const int PixelFrameMax = 32;
    public const int PixelMaxFrames = 256;
    public const int PixelMaxBytes = 2 * 1024 * 1024;
    /// <summary>Per sheet, so one bad sheet does not flood the report.</summary>
    public const int PixelMaxErrors = 12;

    /// <summary>A JSON file that could not be read (its error is already reported); distinct from a JSON null.</summary>
    private static readonly object Unreadable = new();

    private sealed class Report
    {
        public readonly List<string> Errors = new();
        public readonly List<string> Warnings = new();
        public void Err(string m) => Errors.Add(m);
        public void Warn(string m) => Warnings.Add(m);
    }

    // ---- entry points ------------------------------------------------------------------------------------------------

    /// <summary>Validate a pack given as its files: relative POSIX path (<c>pack.json</c>, <c>LICENSE</c>,
    /// <c>sprites/egg.png</c>) to bytes. <paramref name="folderName"/> is the folder the pack lives in or will be
    /// installed to (the catalog entry's id); the pack's <c>id</c> must equal it. Null skips that one check.
    /// <paramref name="strict"/> turns theme design warnings into errors, as the catalog's CI does.</summary>
    public static PackValidation Validate(IReadOnlyDictionary<string, byte[]>? files, string? folderName = null, bool strict = false)
    {
        var rep = new Report();
        PackManifest? manifest = null;
        try
        {
            manifest = Run(rep, files, folderName);
        }
        catch (Exception e)
        {
            // Never throw on bad input: a bug here is a rejected pack, not a crash in the app or the server.
            rep.Err("the validator could not finish this pack (" + e.GetType().Name + "); it is rejected");
        }
        var errors = new List<string>(rep.Errors);
        var warnings = new List<string>(rep.Warnings);
        if (strict)
        {
            errors.AddRange(warnings);
            warnings.Clear();
        }
        return new PackValidation(errors, warnings, errors.Count == 0 ? manifest : null);
    }

    /// <summary>Validate a pack given as its <c>pack.json</c> and the rest of its files (which must not repeat it).</summary>
    public static PackValidation Validate(byte[] packJson, IReadOnlyDictionary<string, byte[]> otherFiles, string? folderName = null, bool strict = false)
    {
        if (otherFiles is not null && otherFiles.ContainsKey("pack.json"))
            return new PackValidation(new[] { "pack.json is given twice" }, Array.Empty<string>(), null);
        var all = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (otherFiles is not null)
            foreach (var kv in otherFiles) all[kv.Key] = kv.Value;
        if (packJson is not null)
            all["pack.json"] = packJson;
        return Validate(all, folderName, strict);
    }

    // ---- one pack ------------------------------------------------------------------------------------------------------

    private static PackManifest? Run(Report rep, IReadOnlyDictionary<string, byte[]>? input, string? folderName)
    {
        if (input is null)
        {
            rep.Err("no files were given");
            return null;
        }
        var onDisk = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var kv in input)
            onDisk[kv.Key ?? ""] = kv.Value ?? Array.Empty<byte>();

        long total = onDisk.Values.Sum(b => (long)b.Length);
        if (total > MaxPackBytes)
            rep.Err($"pack is {PackJson.PyF(total / 1048576.0, 1)} MB, limit is 10 MB");
        if (onDisk.Count > MaxFiles + 3)
            rep.Err($"pack has {onDisk.Count} files, limit is {MaxFiles}");
        foreach (var rel in onDisk.Keys)
        {
            if (!SafeRelPath(rel))
                rep.Err($"{rel}: unsafe file name (segments must match [A-Za-z0-9][A-Za-z0-9._-]*)");
        }

        // -- manifest ----------------------------------------------------------------------------------------------------
        if (!onDisk.TryGetValue("pack.json", out var packBytes))
        {
            rep.Err("pack.json is missing");
            return null;
        }
        object? parsedManifest = LoadJson(rep, packBytes, "pack.json");
        if (ReferenceEquals(parsedManifest, Unreadable))
            return null;
        if (parsedManifest is not PackObject manifest)
        {
            rep.Err("pack.json: must be a JSON object");
            return null;
        }
        foreach (var k in manifest.Keys)
        {
            if (!ManifestRequired.Contains(k) && !ManifestOptional.Contains(k))
                rep.Err($"pack.json: unknown key {PackJson.PyStr(k)}");
        }
        foreach (var k in ManifestRequired)
        {
            if (!manifest.Has(k))
                rep.Err($"pack.json: missing required key {PackJson.PyStr(k)}");
        }

        string? TextField(string key, int lo, int hi)
        {
            if (!manifest.Has(key))
                return null;
            if (manifest[key] is not string v || PackJson.PyLen(v) < lo || PackJson.PyLen(v) > hi)
            {
                rep.Err($"pack.json: {key} must be a string of {lo} to {hi} characters");
                return null;
            }
            if (CtrlRe.IsMatch(v))
            {
                rep.Err($"pack.json: {key} contains control characters");
                return null;
            }
            return v;
        }

        if (manifest.Has("schema_version") && !PackJson.IsOne(manifest["schema_version"]))
            rep.Err("pack.json: schema_version must be 1");
        string? pid = TextField("id", 3, 40);
        if (pid is not null)
        {
            if (!IdRe.IsMatch(pid))
                rep.Err($"pack.json: id {PackJson.PyStr(pid)} must be lowercase kebab-case ([a-z][a-z0-9]*(-[a-z0-9]+)*)");
            else if (folderName is not null && pid != folderName)
                rep.Err($"pack.json: id {PackJson.PyStr(pid)} does not match the folder name {PackJson.PyStr(folderName)}");
        }
        string? name = TextField("name", 1, 40);
        string? version = TextField("version", 1, 32);
        if (version is not null && !SemverRe.IsMatch(version))
            rep.Err($"pack.json: version {PackJson.PyStr(version)} must be MAJOR.MINOR.PATCH");
        string? author = TextField("author", 1, 80);
        string? smin = TextField("sanduhr_min", 1, 32);
        if (smin is not null && !SemverRe.IsMatch(smin))
            rep.Err($"pack.json: sanduhr_min {PackJson.PyStr(smin)} must be MAJOR.MINOR.PATCH");
        string? desc = TextField("description", 1, 200);

        string? kind = manifest["kind"] as string;
        if (manifest.Has("kind") && (kind is null || !Kinds.Contains(kind)))
        {
            rep.Err("pack.json: kind must be one of " + string.Join(", ", Kinds));
            kind = null;
        }

        string? lic = manifest["license"] as string;
        if (manifest.Has("license") && (lic is null || !Licenses.Any(l => l.Id == lic)))
        {
            rep.Err($"pack.json: license {PackJson.PyRepr(manifest["license"])} is not allowed; use one of "
                    + string.Join(", ", Licenses.Select(l => l.Id)));
            lic = null;
        }
        if (kind == "font" && lic is not null && !FontLicenses.Contains(lic))
            rep.Err($"pack.json: font packs must use {string.Join(" or ", FontLicenses)} (got {lic})");

        // trademark scan over the manifest strings and every file name
        foreach (var (where, text) in new[] { ("id", pid), ("name", name), ("description", desc), ("author", author) })
        {
            if (!string.IsNullOrEmpty(text))
                CheckTrademark(rep, "pack.json " + where, text);
        }
        foreach (var rel in onDisk.Keys)
            CheckTrademark(rep, "file name " + PackJson.PyStr(rel), rel);

        object? filesField = manifest["files"];
        var listed = new List<string>();
        if (manifest.Has("files"))
        {
            if (filesField is not List<object?> fl || fl.Count < 1 || fl.Count > MaxFiles)
            {
                rep.Err($"pack.json: files must be an array of 1 to {MaxFiles} paths");
            }
            else
            {
                foreach (var p in fl)
                {
                    if (p is not string ps)
                        rep.Err("pack.json: files entries must be strings");
                    else if (!SafeRelPath(ps))
                        rep.Err($"pack.json: files entry {PackJson.PyStr(ps)} is not a safe relative path");
                    else if (ps is "pack.json" or "LICENSE")
                        rep.Err($"pack.json: files must not list {ps}");
                    else
                        listed.Add(ps);
                }
                if (listed.Distinct(StringComparer.Ordinal).Count() != listed.Count)
                    rep.Err("pack.json: files has duplicate entries");
            }
        }

        string? preview = null;
        if (manifest.Has("preview"))
        {
            if (manifest["preview"] is not string pv || !SafeRelPath(pv) || !pv.EndsWith(".png", StringComparison.Ordinal))
            {
                rep.Err("pack.json: preview must be a safe relative .png path");
            }
            else
            {
                preview = pv;
                if (listed.Contains(pv))
                    rep.Err("pack.json: preview is declared in `preview`; do not repeat it in files");
            }
        }

        // -- exact file listing -------------------------------------------------------------------------------------------
        var declared = new HashSet<string>(listed, StringComparer.Ordinal);
        var exempt = new HashSet<string>(StringComparer.Ordinal) { "pack.json", "LICENSE" };
        if (preview is not null) exempt.Add(preview);
        foreach (var rel in onDisk.Keys)
        {
            if (!declared.Contains(rel) && !exempt.Contains(rel))
                rep.Err($"{rel}: file is not listed in pack.json files");
        }
        foreach (var rel in declared.OrderBy(x => x, StringComparer.Ordinal))
        {
            if (!onDisk.ContainsKey(rel))
                rep.Err($"{rel}: listed in pack.json files but missing from the pack");
        }
        if (preview is not null && !onDisk.ContainsKey(preview))
            rep.Err($"{preview}: preview file is missing");

        // -- per-file content checks ------------------------------------------------------------------------------------
        foreach (var (rel, raw) in onDisk)
        {
            string ext;
            if (rel is "pack.json" or "LICENSE")
            {
                ext = "";
            }
            else
            {
                ext = SplitExt(rel);
                if (ext != ext.ToLowerInvariant())
                    rep.Err($"{rel}: file extension must be lowercase");
                ext = ext.ToLowerInvariant();
                if (BannedExt.Contains(ext))
                {
                    rep.Err($"{rel}: {ext} files are banned (packs are data only: no scripts, executables, HTML or SVG)");
                    continue;
                }
                if (!AllowedExt.Contains(ext))
                {
                    rep.Err($"{rel}: file type {PackJson.PyStr(ext.Length == 0 ? "none" : ext)} is not allowed (allowed: {string.Join(", ", AllowedExt)})");
                    continue;
                }
            }
            foreach (var (sig, what) in ExecSignatures)
            {
                if (raw.AsSpan().StartsWith(sig))
                    rep.Err($"{rel}: content looks like {what}");
            }
            foreach (var snip in BannedSnippets)
            {
                if (ContainsAsciiLower(raw, snip))
                    rep.Err($"{rel}: contains {PackJson.PyStr(snip)}; active content is banned");
            }
            if (ext == ".png")
            {
                if (rel == preview)
                    CheckPng(rep, rel, raw);   // sprite sheets are checked with their frame grid
            }
            else if (ext is ".ttf" or ".otf")
            {
                CheckFont(rep, rel, raw);
            }
        }

        // -- LICENSE ------------------------------------------------------------------------------------------------------
        if (!onDisk.TryGetValue("LICENSE", out var licBytes))
        {
            rep.Err("LICENSE is missing; every pack carries its own license text");
        }
        else if (licBytes.Length == 0)
        {
            rep.Err("LICENSE is empty");
        }
        else if (licBytes.Length > MaxLicenseBytes)
        {
            rep.Err("LICENSE is larger than 100 KB");
        }
        else if (!TryDecodeUtf8(licBytes, out var licText))
        {
            rep.Err("LICENSE is not valid UTF-8");
        }
        else if (lic is not null)
        {
            string flat = string.Join(" ", SplitPyWhitespace(licText.ToLowerInvariant()));
            string phrase = Licenses.First(l => l.Id == lic).Phrase;
            if (!flat.Contains(phrase, StringComparison.Ordinal))
                rep.Err($"LICENSE text does not look like {lic} (expected the standard license text)");
        }

        // -- kind-specific --------------------------------------------------------------------------------------------------
        if (kind is null)
            return null;
        var parsed = new Dictionary<string, object?>(StringComparer.Ordinal);

        // A file's parsed JSON, Unreadable after a reported parse error, or Absent when the pack
        // has no such file.
        object? JLoad(string rel)
        {
            if (!onDisk.TryGetValue(rel, out var bytes))
                return Absent;
            if (!parsed.TryGetValue(rel, out var v))
                parsed[rel] = v = LoadJson(rep, bytes, rel);
            return v;
        }

        // trademark scan over every string in every JSON asset
        foreach (var rel in onDisk.Keys)
        {
            if (!rel.EndsWith(".json", StringComparison.Ordinal) || rel == "pack.json")
                continue;
            object? data = JLoad(rel);
            if (ReferenceEquals(data, Unreadable))
                continue;
            if (FirstTrademarkString(data) is { } s)
                CheckTrademark(rep, $"{rel} value {PackJson.PyStr(PackJson.PySlice(s, 40))}", s);
        }

        switch (kind)
        {
            case "theme":
                if (listed.Count < 1 || listed.Count > 8)
                    rep.Err($"pack.json: theme packs list 1 to 8 theme files (got {listed.Count})");
                foreach (var rel in listed)
                {
                    if (!ThemeFileRe.IsMatch(rel))
                    {
                        rep.Err($"{rel}: theme files must be themes/<kebab-name>.json");
                        continue;
                    }
                    object? data = JLoad(rel);
                    if (ReferenceEquals(data, Absent) || ReferenceEquals(data, Unreadable))
                        continue;
                    var (errs, warns) = LintTheme(data, onDisk[rel]);
                    foreach (var m in errs) rep.Err($"{rel}: {m}");
                    foreach (var m in warns) rep.Warn($"{rel}: {m}");
                }
                break;
            case "palette":
            {
                if (listed.Count != 1 || listed[0] != "palettes.json")
                    rep.Err("pack.json: palette packs must list exactly [\"palettes.json\"]");
                object? data = JLoad("palettes.json");
                if (!ReferenceEquals(data, Absent) && !ReferenceEquals(data, Unreadable))
                    CheckPalettes(rep, data);
                break;
            }
            case "effects":
            {
                if (listed.Count != 1 || listed[0] != "effects.json")
                    rep.Err("pack.json: effects packs must list exactly [\"effects.json\"]");
                object? data = JLoad("effects.json");
                if (!ReferenceEquals(data, Absent) && !ReferenceEquals(data, Unreadable))
                    CheckEffects(rep, data);
                break;
            }
            case "font":
                if (listed.Count < 1 || listed.Count > 4)
                    rep.Err($"pack.json: font packs list 1 to 4 font files (got {listed.Count})");
                foreach (var rel in listed)
                {
                    if (SplitExt(rel) is not (".ttf" or ".otf"))
                        rep.Err($"{rel}: font packs may list only .ttf and .otf files");
                }
                break;
            case "sprite":
            {
                object? data = JLoad("sprite.json");
                if (ReferenceEquals(data, Absent))
                    rep.Err("sprite.json is missing");
                else if (!ReferenceEquals(data, Unreadable))
                    CheckSprite(rep, data, filesField, onDisk, JLoad);
                break;
            }
        }

        if (rep.Errors.Count > 0 || pid is null || name is null || version is null || author is null || lic is null
            || smin is null || desc is null)
            return null;
        return new PackManifest(pid, name, version, author, lic, kind, smin, desc, listed.ToArray(), preview);
    }

    /// <summary>A file the pack does not have (as opposed to one whose JSON is null).</summary>
    private static readonly object Absent = new();

    // ---- small helpers ------------------------------------------------------------------------------------------------

    /// <summary>Parse a UTF-8 JSON file. Returns the value, or <see cref="Unreadable"/> after
    /// reporting why it could not be read (a JSON null included).</summary>
    private static object? LoadJson(Report rep, byte[] raw, string rel)
    {
        if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF)
        {
            rep.Err($"{rel}: has a UTF-8 byte-order mark; JSON must be plain UTF-8");
            return Unreadable;
        }
        if (!TryDecodeUtf8(raw, out _))
        {
            rep.Err($"{rel}: not valid UTF-8");
            return Unreadable;
        }
        if (!PackJson.TryParse(raw, out var value, out var error))
        {
            rep.Err($"{rel}: invalid JSON ({error})");
            return Unreadable;
        }
        // Null means "no usable file" to every caller, so a literal null is an error here, as in the reference.
        if (value is null)
        {
            rep.Err($"{rel}: must be a JSON object, got null");
            return Unreadable;
        }
        return value;
    }

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static bool TryDecodeUtf8(byte[] raw, out string text)
    {
        try
        {
            text = StrictUtf8.GetString(raw);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = "";
            return false;
        }
    }

    /// <summary>Python's <c>str.split()</c>: runs of Unicode whitespace (including the information separators
    /// U+001C to U+001F, which .NET does not count) separate words.</summary>
    private static IEnumerable<string> SplitPyWhitespace(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            if (char.IsWhiteSpace(c) || (c >= '\x1c' && c <= '\x1f'))
            {
                if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); }
            }
            else
            {
                sb.Append(c);
            }
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    /// <summary>Python's <c>os.path.splitext(p)[1]</c> on a POSIX path: the last dot of the last segment, not counting
    /// leading dots (<c>.hidden</c> has no extension).</summary>
    internal static string SplitExt(string p)
    {
        int sep = p.LastIndexOf('/');
        int dot = p.LastIndexOf('.');
        if (dot > sep)
        {
            for (int i = sep + 1; i < dot; i++)
            {
                if (p[i] != '.')
                    return p[dot..];
            }
        }
        return "";
    }

    /// <summary>Whether <paramref name="raw"/>, lowercased as ASCII (bytes.lower), contains the ASCII
    /// <paramref name="needle"/> (already lowercase).</summary>
    private static bool ContainsAsciiLower(byte[] raw, string needle)
    {
        int n = needle.Length;
        int scan = Math.Min(raw.Length, MaxPackBytes);
        for (int i = 0; i + n <= scan; i++)
        {
            int j = 0;
            for (; j < n; j++)
            {
                byte b = raw[i + j];
                if (b >= (byte)'A' && b <= (byte)'Z') b = (byte)(b + 32);
                if (b != (byte)needle[j]) break;
            }
            if (j == n) return true;
        }
        return false;
    }

    /// <summary>True for a relative POSIX path with safe segments.</summary>
    internal static bool SafeRelPath(string? p)
    {
        if (string.IsNullOrEmpty(p) || PackJson.PyLen(p) > 128)
            return false;
        if (p.StartsWith('/') || p.Contains('\\') || p.Contains(':'))
            return false;
        return p.Split('/').All(seg => PathSegRe.IsMatch(seg));
    }

    /// <summary>The trademark the text carries, or null: <c>claude</c> and <c>anthropic</c> anywhere, and
    /// <c>opus</c>, <c>sonnet</c>, <c>haiku</c>, <c>fable</c> as whole words, case-insensitively (FORMAT rule 6).</summary>
    public static string? FindTrademark(string text)
    {
        string lower = text.ToLowerInvariant();
        foreach (var w in TrademarkSubstrings)
        {
            if (lower.Contains(w, StringComparison.Ordinal))
                return w;
        }
        foreach (var w in TrademarkWords)
        {
            int at = 0;
            while ((at = lower.IndexOf(w, at, StringComparison.Ordinal)) >= 0)
            {
                bool before = at > 0 && IsAsciiAlnumLower(lower[at - 1]);
                int end = at + w.Length;
                bool after = end < lower.Length && IsAsciiAlnumLower(lower[end]);
                if (!before && !after)
                    return w;
                at++;
            }
        }
        return null;
    }

    private static bool IsAsciiAlnumLower(char c) => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');

    private static void CheckTrademark(Report rep, string where, string text)
    {
        if (FindTrademark(text) is { } hit)
            rep.Err($"trademark: {where} contains {PackJson.PyStr(hit)}. Third-party marks are not allowed in packs "
                    + "(no Anthropic or Claude marks, no model names as branding); see FORMAT.md rule 6");
    }

    /// <summary>The first string (keys included, in document order) that carries a trademark.</summary>
    private static string? FirstTrademarkString(object? v)
    {
        switch (v)
        {
            case string s:
                return FindTrademark(s) is null ? null : s;
            case PackObject o:
                foreach (var k in o.Keys)
                {
                    if (FindTrademark(k) is not null) return k;
                    if (FirstTrademarkString(o[k]) is { } hit) return hit;
                }
                return null;
            case List<object?> list:
                foreach (var item in list)
                {
                    if (FirstTrademarkString(item) is { } hit) return hit;
                }
                return null;
            default:
                return null;
        }
    }

    private static bool IsHex(object? v) => v is string s && HexRe.IsMatch(s);

    // ---- PNG ----------------------------------------------------------------------------------------------------------
    private static readonly byte[] PngSig = { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A };

    private static int Channels(int ctype) => ctype switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };

    private static bool ValidDepth(int ctype, int depth) => ctype switch
    {
        0 => depth is 1 or 2 or 4 or 8 or 16,
        2 => depth is 8 or 16,
        3 => depth is 1 or 2 or 4 or 8,
        4 => depth is 8 or 16,
        6 => depth is 8 or 16,
        _ => false,
    };

    internal readonly record struct PngInfo(long Width, long Height, int Depth, int ColorType, int Interlace, byte[] Idat);

    /// <summary>Parse a PNG's chunk stream (signature, CRCs, IHDR first, IEND last, nothing after it). Returns null
    /// and the reason for anything malformed.</summary>
    internal static PngInfo? ParsePng(byte[] data, out string error)
    {
        error = "";
        if (data.Length < 8 || !data.AsSpan(0, 8).SequenceEqual(PngSig))
        {
            error = "not a PNG (bad signature)";
            return null;
        }
        long pos = 8;
        bool first = true, seenIend = false;
        var idat = new List<byte>();
        long width = 0, height = 0;
        int depth = 0, ctype = 0, comp = 0, filt = 0, interlace = 0;
        while (pos < data.Length)
        {
            if (pos + 8 > data.Length)
            {
                error = "truncated chunk header";
                return null;
            }
            long length = ReadU32(data, pos);
            string tag = Encoding.Latin1.GetString(data, (int)pos + 4, 4);
            long end = pos + 8 + length + 4;
            if (end > data.Length)
            {
                error = $"truncated {tag} chunk";
                return null;
            }
            uint crc = (uint)ReadU32(data, end - 4);
            if (Crc32(data, (int)pos + 4, (int)(4 + length)) != crc)
            {
                error = $"bad CRC in {tag} chunk";
                return null;
            }
            int body = (int)pos + 8;
            if (first)
            {
                if (tag != "IHDR" || length != 13)
                {
                    error = "first chunk must be a 13-byte IHDR";
                    return null;
                }
                width = ReadU32(data, body);
                height = ReadU32(data, body + 4);
                depth = data[body + 8];
                ctype = data[body + 9];
                comp = data[body + 10];
                filt = data[body + 11];
                interlace = data[body + 12];
                first = false;
            }
            else if (tag == "IHDR")
            {
                error = "duplicate IHDR";
                return null;
            }
            else if (tag == "IDAT")
            {
                idat.AddRange(new ArraySegment<byte>(data, body, (int)length));
            }
            else if (tag == "IEND")
            {
                seenIend = true;
                pos = end;
                break;
            }
            pos = end;
        }
        if (first) { error = "no IHDR chunk"; return null; }
        if (!seenIend) { error = "missing IEND chunk"; return null; }
        if (pos != data.Length) { error = "trailing data after IEND"; return null; }
        if (width == 0 || height == 0) { error = "zero-sized image"; return null; }
        if (Channels(ctype) == 0 || !ValidDepth(ctype, depth))
        {
            error = $"invalid color type {ctype} / bit depth {depth}";
            return null;
        }
        if (comp != 0 || filt != 0 || interlace is not (0 or 1))
        {
            error = "unsupported compression, filter or interlace method";
            return null;
        }
        if (idat.Count == 0) { error = "no IDAT chunk"; return null; }
        return new PngInfo(width, height, depth, ctype, interlace, idat.ToArray());
    }

    private static long ReadU32(byte[] d, long at)
        => ((long)d[at] << 24) | ((long)d[at + 1] << 16) | ((long)d[at + 2] << 8) | d[at + 3];

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    /// <summary>The CRC-32 PNG chunks carry (zlib's crc32).</summary>
    internal static uint Crc32(byte[] data, int offset, int count)
    {
        uint c = 0xFFFFFFFFu;
        for (int i = offset; i < offset + count; i++)
            c = CrcTable[(c ^ data[i]) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    /// <summary>Full PNG check. Returns (w, h), or null after reporting errors.</summary>
    private static (long W, long H)? CheckPng(Report rep, string rel, byte[] data)
    {
        if (data.Length > MaxImageBytes)
        {
            rep.Err($"{rel}: image is {PackJson.PyF(data.Length / 1048576.0, 1)} MB, limit is 2 MB");
            return null;
        }
        if (ParsePng(data, out var why) is not { } png)
        {
            rep.Err($"{rel}: {why}");
            return null;
        }
        long w = png.Width, h = png.Height;
        if (w > MaxImageSide || h > MaxImageSide)
        {
            rep.Err($"{rel}: image is {w}x{h}, limit is {MaxImageSide} pixels on a side");
            return null;
        }
        if (png.Interlace == 0)
        {
            long bits = png.Depth * Channels(png.ColorType);
            long expected = h * (1 + (w * bits + 7) / 8);
            long got;
            try
            {
                got = InflatedLength(png.Idat, expected + 1);
            }
            catch (Exception e) when (e is InvalidDataException or NotSupportedException or ArgumentException)
            {
                rep.Err($"{rel}: corrupt image data ({e.Message})");
                return null;
            }
            if (got != expected)
            {
                rep.Err($"{rel}: image data is {got} bytes, expected {expected} for {w}x{h}");
                return null;
            }
        }
        return (w, h);
    }

    /// <summary>How many bytes the zlib stream inflates to, reading at most <paramref name="limit"/>. In memory only.</summary>
    private static long InflatedLength(byte[] zlib, long limit)
    {
        using var input = new MemoryStream(zlib, writable: false);
        using var z = new ZLibStream(input, CompressionMode.Decompress);
        var buf = new byte[64 * 1024];
        long total = 0;
        while (total < limit)
        {
            int n = z.Read(buf, 0, (int)Math.Min(buf.Length, limit - total));
            if (n == 0) break;
            total += n;
        }
        return total;
    }

    private static void CheckFont(Report rep, string rel, byte[] data)
    {
        if (data.Length > MaxFontBytes)
        {
            rep.Err($"{rel}: font is {PackJson.PyF(data.Length / 1048576.0, 1)} MB, limit is 5 MB");
            return;
        }
        ReadOnlySpan<byte> magic = data.AsSpan(0, Math.Min(4, data.Length));
        if (magic.SequenceEqual("ttcf"u8))
            rep.Err($"{rel}: font collections (.ttc) are not allowed");
        else if (!(magic.SequenceEqual(new byte[] { 0, 1, 0, 0 }) || magic.SequenceEqual("true"u8) || magic.SequenceEqual("OTTO"u8)))
            rep.Err($"{rel}: not a TrueType/OpenType font (bad magic number)");
    }

    // ---- theme --------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A catalog theme (FORMAT 5.1): Core's <see cref="ThemeLint"/> (the app's own rules, so the catalog and the
    /// paste box agree) plus what FORMAT adds for the catalog, which the app's lint lets through for its own themes:
    /// unknown keys, <c>accent_bloom: null</c>, a non-boolean <c>opts_out_of_mica</c> or <c>bg_grid</c>, and a
    /// non-string font name. As in the reference, the design warnings are measured only when there is no error.
    /// </summary>
    internal static (List<string> Errors, List<string> Warnings) LintTheme(object? data, byte[] raw)
    {
        var errs = new List<string>();
        var warns = new List<string>();
        if (data is not PackObject o)
        {
            errs.Add("theme must be a JSON object");
            return (errs, warns);
        }
        foreach (var k in o.Keys)
        {
            if (k != "name" && !ThemeColorFields.Contains(k) && !ThemeOptionalKeys.Contains(k))
                errs.Add($"unknown key {PackJson.PyStr(k)} (catalog themes allow only the documented fields)");
        }
        if (o.Has("accent_bloom") && o["accent_bloom"] is null)
            errs.Add("accent_bloom must be an object {\"blur\": 3-8, \"alpha\": 0.25-0.65}");
        foreach (var key in new[] { "opts_out_of_mica", "bg_grid" })
        {
            if (o.Has(key) && o[key] is not bool)
                errs.Add($"{key} must be true or false");
        }
        foreach (var key in new[] { "monospace_font", "monospace_fallback" })
        {
            if (o.Has(key) && o[key] is not string)
                errs.Add($"{key} must be a string");
        }

        ThemeLintResult core = ThemeLint.Lint(Encoding.UTF8.GetString(raw));
        errs.AddRange(core.Errors.Select(f => f.Message));
        if (errs.Count == 0)
            warns.AddRange(core.Warnings.Select(f => f.Message));
        return (errs, warns);
    }

    // ---- palette and effects --------------------------------------------------------------------------------------------

    private static void CheckPalettes(Report rep, object? data)
    {
        const string rel = "palettes.json";
        if (data is not PackObject o)
        {
            rep.Err($"{rel}: must be a JSON object");
            return;
        }
        var extra = o.Keys.Where(k => k is not ("schema_version" or "palettes")).OrderBy(k => k, StringComparer.Ordinal).ToList();
        if (extra.Count > 0)
            rep.Err($"{rel}: unknown keys {PackJson.PyList(extra)}");
        if (!PackJson.IsOne(o["schema_version"]))
            rep.Err($"{rel}: schema_version must be 1");
        if (o["palettes"] is not List<object?> items || items.Count < 1 || items.Count > 64)
        {
            rep.Err($"{rel}: palettes must be an array of 1 to 64 entries");
            return;
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < items.Count; i++)
        {
            string where = $"{rel}: palettes[{i}]";
            if (items[i] is not PackObject p)
            {
                rep.Err($"{where} must be an object");
                continue;
            }
            var pextra = p.Keys.Where(k => k is not ("name" or "colors" or "mood")).OrderBy(k => k, StringComparer.Ordinal).ToList();
            if (pextra.Count > 0)
                rep.Err($"{where} has unknown keys {PackJson.PyList(pextra)}");
            if (p["name"] is not string n || !PresetNameRe.IsMatch(n))
                rep.Err($"{where}: name must be 2 to 24 lowercase letters, digits or hyphens");
            else if (!seen.Add(n))
                rep.Err($"{where}: duplicate palette name {PackJson.PyStr(n)}");
            if (p["colors"] is not List<object?> cols || cols.Count < 1 || cols.Count > 4)
            {
                rep.Err($"{where}: colors must be 1 to 4 #rrggbb stops");
            }
            else
            {
                foreach (var c in cols)
                {
                    if (!IsHex(c))
                        rep.Err($"{where}: {PackJson.PyRepr(c)} is not a #rrggbb color");
                }
            }
            if (p.Has("mood") && (p["mood"] is not string mood || PackJson.PyLen(mood) > 24))
                rep.Err($"{where}: mood must be a string of at most 24 characters");
        }
    }

    private static void CheckEffects(Report rep, object? data)
    {
        const string rel = "effects.json";
        if (data is not PackObject o)
        {
            rep.Err($"{rel}: must be a JSON object");
            return;
        }
        var extra = o.Keys.Where(k => k is not ("schema_version" or "effects")).OrderBy(k => k, StringComparer.Ordinal).ToList();
        if (extra.Count > 0)
            rep.Err($"{rel}: unknown keys {PackJson.PyList(extra)}");
        if (!PackJson.IsOne(o["schema_version"]))
            rep.Err($"{rel}: schema_version must be 1");
        if (o["effects"] is not List<object?> items || items.Count < 1 || items.Count > 64)
        {
            rep.Err($"{rel}: effects must be an array of 1 to 64 presets");
            return;
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < items.Count; i++)
        {
            string where = $"{rel}: effects[{i}]";
            if (items[i] is not PackObject e)
            {
                rep.Err($"{where} must be an object");
                continue;
            }
            var eextra = e.Keys.Where(k => k is not ("name" or "type" or "intensity" or "period_ms" or "color"))
                .OrderBy(k => k, StringComparer.Ordinal).ToList();
            if (eextra.Count > 0)
                rep.Err($"{where} has unknown keys {PackJson.PyList(eextra)}");
            if (e["name"] is not string n || !PresetNameRe.IsMatch(n))
                rep.Err($"{where}: name must be 2 to 24 lowercase letters, digits or hyphens");
            else if (!seen.Add(n))
                rep.Err($"{where}: duplicate preset name {PackJson.PyStr(n)}");
            if (e["type"] is not string t || !EffectTypes.Contains(t))
                rep.Err($"{where}: type must be one of {string.Join(", ", EffectTypes)}");
            if (!PackJson.IsNum(e["intensity"]) || ((PackNumber)e["intensity"]!).Value is < 0 or > 1)
                rep.Err($"{where}: intensity must be a number from 0 to 1");
            if (!PackJson.IsInt(e["period_ms"]) || ((PackNumber)e["period_ms"]!).Value is < 400 or > 10000)
                rep.Err($"{where}: period_ms must be an integer from 400 to 10000");
            if (!(e["color"] is "ink" || IsHex(e["color"])))
                rep.Err($"{where}: color must be a #rrggbb color or \"ink\"");
        }
    }

    // ---- sprite -------------------------------------------------------------------------------------------------------

    /// <summary>Check a sprite v2 pixel sheet (FORMAT 5.5) drawn into a <paramref name="w"/> x <paramref name="h"/>
    /// cell. <paramref name="maxIndex"/> is the highest frame index the animations use (-1 for none). Returns the
    /// messages, capped at <see cref="PixelMaxErrors"/> plus one line saying more were cut.</summary>
    internal static List<string> CheckPixelSheet(string rel, object? data, long w, long h, double maxIndex, string label)
    {
        var errors = new List<string>();
        void Err(string msg) => errors.Add($"{rel}: {msg}");

        if (data is not PackObject o)
            return new List<string> { $"{rel}: a pixel sheet must be a JSON object with {string.Join(", ", PixelKeys)}" };
        var extra = o.Keys.Where(k => !PixelKeys.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        if (extra.Count > 0)
            Err($"unknown keys {PackJson.PyList(extra)} (a pixel sheet has exactly {string.Join(", ", PixelKeys)})");
        foreach (var k in PixelKeys)
        {
            if (!o.Has(k))
                Err($"missing required key {PackJson.PyStr(k)}");
        }

        int? ncolors = null;
        if (o.Has("palette"))
        {
            if (o["palette"] is not List<object?> pal || pal.Count < 1 || pal.Count > PixelMaxColors)
            {
                string got = o["palette"] is List<object?> pl ? $" (got {pl.Count})" : "";
                Err($"palette must be 1 to {PixelMaxColors} #rrggbb colors{got}");
            }
            else
            {
                ncolors = pal.Count;
                for (int i = 0; i < pal.Count; i++)
                {
                    if (!IsHex(pal[i]))
                        Err($"palette[{i}] {PackJson.PyRepr(pal[i])} is not a #rrggbb color");
                    else if (((string)pal[i]!).Equals("#ffffff", StringComparison.OrdinalIgnoreCase))
                        Err($"palette[{i}] is pure white ({pal[i]}), which sprites may not use (it burns on "
                            + "OLED panels); use an off-white such as #f0f0f0");
                }
            }
        }

        object? fwv = o["frame_w"], fhv = o["frame_h"];
        bool sizeOk = PixelSize(fwv) && PixelSize(fhv);
        long? fw = sizeOk ? (long)((PackNumber)fwv!).Value : null;
        long? fh = sizeOk ? (long)((PackNumber)fhv!).Value : null;
        if (o.Has("frame_w") && o.Has("frame_h"))
        {
            if (!sizeOk)
                Err($"frame_w and frame_h must be integers from {PixelFrameMin} to {PixelFrameMax}, got "
                    + $"{PackJson.PyRepr(fwv)} x {PackJson.PyRepr(fhv)} (pixel sheets are sized for a companion)");
            else if (fw != w || fh != h)
                Err($"frame size {fw}x{fh} does not match the {label} frame size {w}x{h}");
        }

        if (o.Has("frames"))
        {
            if (o["frames"] is not List<object?> frames || frames.Count < 1 || frames.Count > PixelMaxFrames)
            {
                Err($"frames must be an array of 1 to {PixelMaxFrames} frames");
            }
            else
            {
                if (maxIndex >= frames.Count)
                    Err($"animations use frame index {FormatIndex(maxIndex)} but the pixel sheet has only {frames.Count} "
                        + $"frame{(frames.Count == 1 ? "" : "s")} ({label})");
                for (int fi = 0; fi < frames.Count; fi++)
                {
                    if (frames[fi] is not List<object?> frame || !frame.All(r => r is string))
                    {
                        Err($"frames[{fi}] must be an array of strings, one per pixel row");
                        continue;
                    }
                    if (fh is not null && frame.Count != fh)
                        Err($"frames[{fi}] has {frame.Count} rows, expected {fh} (frame_h)");
                    for (int ri = 0; ri < frame.Count; ri++)
                    {
                        string row = (string)frame[ri]!;
                        int rowLen = PackJson.PyLen(row);
                        if (fw is not null && rowLen != fw)
                            Err($"frames[{fi}][{ri}] is {rowLen} characters, expected {fw} (frame_w)");
                        int ci = 0;
                        foreach (var rune in row.EnumerateRunes())
                        {
                            int c = rune.Value;
                            if (c > 0x7f || PixelChars.IndexOf((char)c) < 0)
                            {
                                Err($"frames[{fi}][{ri}][{ci}] is {PackJson.PyStr(rune.ToString())}; a pixel is '.' "
                                    + "(transparent) or 0-9, a-f (a palette index, lowercase)");
                                break;
                            }
                            if (c != '.' && ncolors is not null && PixelIndex((char)c) >= ncolors)
                            {
                                int idx = PixelIndex((char)c);
                                Err($"frames[{fi}][{ri}][{ci}] is palette index {PackJson.PyStr(rune.ToString())} ({idx}) "
                                    + $"but the palette has only {ncolors} color{(ncolors == 1 ? "" : "s")}");
                                break;
                            }
                            ci++;
                        }
                    }
                    if (errors.Count > PixelMaxErrors)
                        break;
                }
            }
        }

        if (errors.Count <= PixelMaxErrors)
            return errors;
        var capped = errors.Take(PixelMaxErrors).ToList();
        capped.Add($"{rel}: more errors in this pixel sheet are not shown; fix the ones above first");
        return capped;
    }

    private static bool PixelSize(object? v)
        => PackJson.IsInt(v) && ((PackNumber)v!).Value is >= PixelFrameMin and <= PixelFrameMax;

    /// <summary>The palette index a pixel character names ('0'-'9', 'a'-'f').</summary>
    internal static int PixelIndex(char c) => c <= '9' ? c - '0' : c - 'a' + 10;

    private static string FormatIndex(double v) => v.ToString("0", CultureInfo.InvariantCulture);

    private static bool SpriteSize(object? v) => PackJson.IsInt(v) && ((PackNumber)v!).Value is >= 8 and <= 64;

    private static void CheckSprite(Report rep, object? data, object? filesField, IReadOnlyDictionary<string, byte[]> onDisk,
        Func<string, object?> jload)
    {
        const string rel = "sprite.json";
        if (data is not PackObject o)
        {
            rep.Err($"{rel}: must be a JSON object");
            return;
        }
        string[] allowed = { "schema_version", "frame_w", "frame_h", "scale", "fps", "sheet", "stages", "animations", "moods", "triggers", "roam" };
        var extra = o.Keys.Where(k => !allowed.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        if (extra.Count > 0)
            rep.Err($"{rel}: unknown keys {PackJson.PyList(extra)}");
        int? sv = null;
        if (PackJson.IsInt(o["schema_version"]) && ((PackNumber)o["schema_version"]!).Value is 1 or 2)
            sv = (int)((PackNumber)o["schema_version"]!).Value;
        else
            rep.Err($"{rel}: schema_version must be 1 or 2 (2 allows .json pixel sheets)");

        string? SheetPathError(object? path)
        {
            if (path is not string p || !SafeRelPath(p))
                return "must be a safe relative path";
            if (p.EndsWith(".png", StringComparison.Ordinal))
                return null;
            if (p.EndsWith(".json", StringComparison.Ordinal))
            {
                if (p == "sprite.json")
                    return "must not be sprite.json itself";
                if (sv == 2)
                    return null;
                return $"{PackJson.PyStr(p)} is a .json pixel sheet, which needs sprite.json schema_version 2 "
                       + "(schema_version 1 sheets are .png only)";
            }
            return "must be a .png sheet" + (sv == 2 ? " or a .json pixel sheet" : "");
        }

        long? fw = null, fh = null;
        if (!SpriteSize(o["frame_w"]) || !SpriteSize(o["frame_h"]))
            rep.Err($"{rel}: frame_w and frame_h must be integers from 8 to 64");
        else
        {
            fw = (long)((PackNumber)o["frame_w"]!).Value;
            fh = (long)((PackNumber)o["frame_h"]!).Value;
        }
        if (!PackJson.IsInt(o["scale"]) || ((PackNumber)o["scale"]!).Value is < 1 or > 4)
            rep.Err($"{rel}: scale must be an integer from 1 to 4");
        if (!PackJson.IsInt(o["fps"]) || ((PackNumber)o["fps"]!).Value is < 1 or > 24)
            rep.Err($"{rel}: fps must be an integer from 1 to 24");

        // animations
        double maxIndex = -1;
        var animNames = new HashSet<string>(StringComparer.Ordinal);
        if (o["animations"] is not PackObject anims)
        {
            rep.Err($"{rel}: animations must be an object");
        }
        else
        {
            if (anims.Count > 32)
                rep.Err($"{rel}: more than 32 animations");
            foreach (var name in anims.Keys)
            {
                if (!AnimNameRe.IsMatch(name))
                    rep.Err($"{rel}: animation name {PackJson.PyStr(name)} must match [a-z][a-z0-9_]{{1,23}}");
                animNames.Add(name);
                if (anims[name] is not List<object?> frames || frames.Count < 1 || frames.Count > 64
                    || !frames.All(i => PackJson.IsInt(i) && ((PackNumber)i!).Value >= 0))
                    rep.Err($"{rel}: animation {PackJson.PyStr(name)} must be 1 to 64 non-negative integer frame indexes");
                else
                    maxIndex = Math.Max(maxIndex, frames.Max(i => ((PackNumber)i!).Value));
            }
            foreach (var need in RequiredAnimations)
            {
                if (!animNames.Contains(need))
                    rep.Err($"{rel}: required animation {PackJson.PyStr(need)} is missing");
            }
        }

        // moods and triggers
        foreach (var (key, keys) in new[] { ("moods", MoodKeys), ("triggers", TriggerKeys) })
        {
            if (o[key] is not PackObject m)
            {
                rep.Err($"{rel}: {key} must be an object with keys {string.Join(", ", keys)}");
                continue;
            }
            foreach (var k in keys)
            {
                if (!m.Has(k))
                    rep.Err($"{rel}: {key} is missing {PackJson.PyStr(k)}");
                else if (m[k] is not string an || !animNames.Contains(an))
                    rep.Err($"{rel}: {key}.{k} names animation {PackJson.PyRepr(m[k])}, which is not defined");
            }
            foreach (var k in m.Keys)
            {
                if (!keys.Contains(k))
                    rep.Err($"{rel}: {key} has unknown key {PackJson.PyStr(k)}");
            }
        }

        // roam
        if (o["roam"] is not PackObject roam)
        {
            rep.Err($"{rel}: roam must be an object");
        }
        else
        {
            var rextra = roam.Keys.Where(k => k is not ("edges" or "speed_px_s")).OrderBy(k => k, StringComparer.Ordinal).ToList();
            if (rextra.Count > 0)
                rep.Err($"{rel}: roam has unknown keys {PackJson.PyList(rextra)}");
            if (roam["edges"] is not List<object?> edges || edges.Count == 0
                || !edges.All(e => e is string s && RoamEdges.Contains(s))
                || edges.Cast<string>().Distinct(StringComparer.Ordinal).Count() != edges.Count)
                rep.Err($"{rel}: roam.edges must be a non-empty unique subset of {string.Join(", ", RoamEdges)}");
            if (!PackJson.IsNum(roam["speed_px_s"]) || ((PackNumber)roam["speed_px_s"]!).Value is < 5 or > 120)
                rep.Err($"{rel}: roam.speed_px_s must be a number from 5 to 120");
        }

        // sheets and stages
        var sheets = new List<(string Path, long W, long H, string Label)>();
        bool hasSheet = o.Has("sheet"), hasStages = o.Has("stages");
        if (hasSheet == hasStages)
        {
            rep.Err($"{rel}: give exactly one of `sheet` (single sheet) or `stages`");
        }
        else if (hasSheet)
        {
            object? s = o["sheet"];
            if (SheetPathError(s) is { } why)
                rep.Err($"{rel}: sheet {why}");
            else if (fw is not null)
                sheets.Add(((string)s!, fw.Value, fh!.Value, "sheet"));
        }
        else
        {
            var stages = o["stages"] as List<object?>;
            if (stages is null || stages.Count < 1 || stages.Count > 6)
            {
                rep.Err($"{rel}: stages must be 1 to 6 entries");
                stages = new List<object?>();
            }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var used = new HashSet<string>(StringComparer.Ordinal);
            double? prev = null;
            for (int i = 0; i < stages.Count; i++)
            {
                string where = $"{rel}: stages[{i}]";
                if (stages[i] is not PackObject st)
                {
                    rep.Err($"{where} must be an object");
                    continue;
                }
                var sextra = st.Keys.Where(k => k is not ("id" or "name" or "min_growth" or "sheet" or "frame_w" or "frame_h"))
                    .OrderBy(k => k, StringComparer.Ordinal).ToList();
                if (sextra.Count > 0)
                    rep.Err($"{where} has unknown keys {PackJson.PyList(sextra)}");
                object? sid = st["id"];
                if (sid is not string sids || !IdRe.IsMatch(sids) || PackJson.PyLen(sids) < 2 || PackJson.PyLen(sids) > 20)
                    rep.Err($"{where}: id must be kebab-case, 2 to 20 characters");
                else if (!ids.Add(sids))
                    rep.Err($"{where}: duplicate stage id {PackJson.PyStr(sids)}");
                if (st["name"] is not string nm || PackJson.PyLen(nm) < 1 || PackJson.PyLen(nm) > 24)
                    rep.Err($"{where}: name must be 1 to 24 characters");
                if (!PackJson.IsNum(st["min_growth"]) || ((PackNumber)st["min_growth"]!).Value is < 0 or > 1)
                {
                    rep.Err($"{where}: min_growth must be a number from 0 to 1");
                }
                else
                {
                    double mg = ((PackNumber)st["min_growth"]!).Value;
                    if (i == 0 && mg != 0)
                        rep.Err($"{where}: the first stage must have min_growth 0");
                    if (prev is not null && mg <= prev)
                        rep.Err($"{where}: min_growth {PackJson.PyG(mg)} must be greater than the previous stage's {PackJson.PyG(prev.Value)}");
                    prev = mg;
                }
                long? sw = null, sh = null;
                if (!SpriteSize(st["frame_w"]) || !SpriteSize(st["frame_h"]))
                {
                    rep.Err($"{where}: frame_w and frame_h must be integers from 8 to 64");
                }
                else
                {
                    sw = (long)((PackNumber)st["frame_w"]!).Value;
                    sh = (long)((PackNumber)st["frame_h"]!).Value;
                    if (i == 0 && fw is not null && (sw != fw || sh != fh))
                        rep.Err($"{where}: stage 0 frame size {sw}x{sh} must equal the top-level frame_w x frame_h {fw}x{fh}");
                }
                object? sp = st["sheet"];
                if (SheetPathError(sp) is { } why)
                    rep.Err($"{where}: sheet {why}");
                else if (!used.Add((string)sp!))
                    rep.Err($"{where}: sheet {PackJson.PyStr((string)sp!)} is used by another stage");
                else if (sw is not null)
                    sheets.Add(((string)sp!, sw.Value, sh!.Value, "stage " + PackJson.PyRepr(sid)));
            }
        }

        var expectedFiles = new HashSet<string>(StringComparer.Ordinal) { "sprite.json" };
        foreach (var s in sheets) expectedFiles.Add(s.Path);
        var listed = new HashSet<string>(StringComparer.Ordinal);
        if (filesField is List<object?> fl)
            foreach (var p in fl.OfType<string>()) listed.Add(p);
        if (sheets.Count > 0 && !listed.SetEquals(expectedFiles))
        {
            var missing = expectedFiles.Except(listed).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var surplus = listed.Except(expectedFiles).OrderBy(x => x, StringComparer.Ordinal).ToList();
            if (missing.Count > 0)
                rep.Err("pack.json: files must list " + string.Join(", ", missing));
            if (surplus.Count > 0)
                rep.Err("pack.json: sprite packs may list only sprite.json and the sheets it references; remove "
                        + string.Join(", ", surplus));
        }

        foreach (var (path, w, h, label) in sheets)
        {
            if (!onDisk.TryGetValue(path, out var bytes))
                continue;   // reported by the exact-listing check
            if (path.EndsWith(".json", StringComparison.Ordinal))
            {
                if (bytes.Length > PixelMaxBytes)
                {
                    rep.Err($"{path}: pixel sheet is {PackJson.PyF(bytes.Length / 1048576.0, 1)} MB, limit is 2 MB");
                    continue;
                }
                object? sheet = jload(path);
                if (!ReferenceEquals(sheet, Unreadable) && !ReferenceEquals(sheet, Absent))
                    foreach (var m in CheckPixelSheet(path, sheet, w, h, maxIndex, label)) rep.Err(m);
                continue;
            }
            if (CheckPng(rep, path, bytes) is not { } dims)
                continue;
            var (pw, ph) = dims;
            if (pw % w != 0 || ph % h != 0)
            {
                rep.Err($"{path}: sheet is {pw}x{ph}, not a whole number of {w}x{h} frames ({label})");
                continue;
            }
            long cells = (pw / w) * (ph / h);
            if (maxIndex >= cells)
                rep.Err($"{path}: animations use frame index {FormatIndex(maxIndex)} but the sheet has only {cells} frames ({label})");
        }
    }
}
