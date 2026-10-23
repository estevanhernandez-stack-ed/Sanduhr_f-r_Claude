using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>A widget pack Claude proposed, built in memory and passed by <see cref="PackValidator"/> (strict): its files
/// (relative POSIX path to bytes, <c>pack.json</c> and <c>LICENSE</c> included) and the validator's manifest.</summary>
public sealed record WidgetProposalPack(IReadOnlyDictionary<string, byte[]> Files, PackManifest Manifest)
{
    public string Id => Manifest.Id;
    public string Kind => Manifest.Kind;
    public string Name => Manifest.Name;
}

/// <summary>What <see cref="WidgetProposal.Build"/> made of the tool's arguments: the pack, or the reasons it was refused.</summary>
public sealed record WidgetBuild(WidgetProposalPack? Pack, IReadOnlyList<DeskReason> Reasons)
{
    public bool Ok => Pack is not null;
}

/// <summary>What <see cref="WidgetProposal.Decode"/> made of a request file.</summary>
public enum WidgetRequestKind { Proposal, Refused, Discard }

/// <summary>One decoded request: a proposal (the pack checked again), a refusal with reasons, or nothing to answer.</summary>
public sealed record WidgetRequest(WidgetRequestKind Kind, string? Id, WidgetProposalPack? Pack, string? Note, IReadOnlyList<DeskReason> Reasons)
{
    public static WidgetRequest Discard { get; } = new(WidgetRequestKind.Discard, null, null, null, []);
}

/// <summary>What the widget tells the MCP server's caller about a proposed widget.</summary>
public sealed record WidgetAnswer(
    string Status,
    string? Reason = null,
    IReadOnlyList<DeskReason>? Reasons = null,
    string? PackId = null,
    bool? Companion = null)
{
    public const string Previewing = "previewing";
    public const string Applied = "applied";
    public const string Rejected = "rejected";

    /// <summary>The statuses the widget may give (the server passes nothing else on).</summary>
    public static readonly IReadOnlyList<string> Statuses = [Previewing, Applied, Rejected];

    public static WidgetAnswer Refused(IReadOnlyList<DeskReason> reasons) => new(Rejected, Reasons: reasons);

    /// <summary>What a Sanduhr with no review wired answers a good proposal (tests, the harness).</summary>
    public static WidgetAnswer NotReady { get; } = new(Rejected, "not_ready",
        [new DeskReason("widget", "not_ready: this version of Sanduhr checks widgets but cannot preview them here")]);
}

/// <summary>
/// Widgets made with Claude (spec 3.8 section 5, <c>propose_widget</c>), the pure half both sides share. The MCP server turns the
/// tool's arguments into a whole pack in memory with <see cref="Build"/>: <c>pack.json</c> generated from the given fields (the id
/// namespaced <c>my-</c>), a <c>LICENSE</c> text for the chosen license (CC0-1.0 by default), and the kind's JSON files (a sprite's
/// <c>sprite.json</c> and its pixel sheets, <c>palettes.json</c>, or <c>effects.json</c>). Data only: no PNG and no font is ever
/// accepted, and a theme goes through <c>propose_theme</c>. The pack must pass <see cref="PackValidator"/> in strict mode (the
/// trademark rule included) before anything is written. The server then queues <c>widget-request.json</c> (<see cref="RequestJson"/>)
/// and the widget reads it with <see cref="Decode"/>, which checks everything again: the widget never trusts the server.
///
/// Pure: JSON in, bytes and reasons out. No disk, no network, no process.
/// </summary>
public static class WidgetProposal
{
    public const string RequestFile = "widget-request.json";
    public const string ResultFile = "widget-result.json";
    public const int SchemaVersion = 1;

    /// <summary>A request older than this (the widget was not running) is dropped unanswered, as the design handoff does.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(600);

    /// <summary>The request file's cap. A Claude-made pack is JSON only, so this is far above a real one.</summary>
    public const int MaxRequestBytes = 4 * 1024 * 1024;

    /// <summary>Every Claude-made pack id starts with this, so it can never take a catalog pack's id.</summary>
    public const string IdPrefix = "my-";

    public const string DefaultLicense = "CC0-1.0";
    public const string DefaultVersion = "1.0.0";
    public const string DefaultAuthor = "Made here";

    /// <summary>The first Sanduhr that installs Claude-made widgets.</summary>
    public const string SanduhrMin = "3.8.0";

    /// <summary>The kinds Claude may propose here. <c>theme</c> has its own tool, and <c>font</c> is a binary.</summary>
    public static readonly IReadOnlyList<string> Kinds = ["sprite", "palette", "effects"];

    /// <summary>The licenses a Claude-made pack may carry (FORMAT section 4, less the font license).</summary>
    public static readonly IReadOnlyList<string> Licenses = ["CC0-1.0", "CC-BY-4.0", "MIT", "Apache-2.0", "BSD-3-Clause", "ISC"];

    private static readonly string[] Args = ["kind", "pack", "sprite", "sheets", "palettes", "effects", "note"];
    private static readonly string[] PackArgs = ["id", "name", "description", "version", "author", "license"];

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary><paramref name="id"/> with the <c>my-</c> prefix (once).</summary>
    public static string NamespacedId(string id) => id.StartsWith(IdPrefix, StringComparison.Ordinal) ? id : IdPrefix + id;

    // ---- the server's half: arguments to a pack ----------------------------------------------------------------------

    /// <summary>Builds and validates the pack the tool's arguments describe. <paramref name="now"/> stamps the LICENSE year.</summary>
    public static WidgetBuild Build(JsonObject? args, DateTimeOffset now)
    {
        args ??= new JsonObject();
        var reasons = new List<DeskReason>();
        var unknown = args.Select(kv => kv.Key).Where(k => !Args.Contains(k, StringComparer.Ordinal)).Order(StringComparer.Ordinal).ToList();
        if (unknown.Count > 0)
            reasons.Add(new DeskReason("arguments", "unknown argument(s): " + string.Join(", ", unknown.Take(5).Select(k => Clip(k, 24)))
                + ". The arguments are kind, pack, sprite, sheets, palettes, effects and note. Claude never supplies a PNG or a font."));

        string? kind = Str(args["kind"]);
        switch (kind)
        {
            case null:
                reasons.Add(new DeskReason("kind", "must be one of sprite, palette, effects"));
                break;
            case "theme":
                reasons.Add(new DeskReason("kind", "a theme goes through propose_theme (it previews and saves the widget theme the same way); "
                    + "propose_widget makes sprite, palette and effects packs"));
                break;
            case "font":
                reasons.Add(new DeskReason("kind", "fonts are binaries and are never proposed; the user installs a font pack from the catalog"));
                break;
            default:
                if (!Kinds.Contains(kind, StringComparer.Ordinal))
                    reasons.Add(new DeskReason("kind", "must be one of sprite, palette, effects"));
                break;
        }

        string? note = null;
        if (args["note"] is { } noteNode)
        {
            note = Str(noteNode);
            if (note is null || note.Length > 200 || note.Any(char.IsControl))
                reasons.Add(new DeskReason("note", "must be one line of at most 200 characters"));
        }

        // pack.json fields
        string? id = null, name = null, description = null, version = DefaultVersion, author = DefaultAuthor, license = DefaultLicense;
        if (args["pack"] is not JsonObject pack)
        {
            reasons.Add(new DeskReason("pack", "must be an object: {id, name, description, version?, author?, license?}"));
        }
        else
        {
            var extra = pack.Select(kv => kv.Key).Where(k => !PackArgs.Contains(k, StringComparer.Ordinal)).Order(StringComparer.Ordinal).ToList();
            if (extra.Count > 0)
                reasons.Add(new DeskReason("pack", "unknown key(s): " + string.Join(", ", extra.Take(5).Select(k => Clip(k, 24)))
                    + ". pack.json's files, kind, schema_version and sanduhr_min are written by Sanduhr."));
            id = Str(pack["id"]);
            if (id is null) reasons.Add(new DeskReason("pack.id", "must be a lowercase kebab-case string; Sanduhr adds the my- prefix"));
            else id = NamespacedId(id);
            name = Str(pack["name"]);
            if (name is null) reasons.Add(new DeskReason("pack.name", "must be a string of 1 to 40 characters"));
            description = Str(pack["description"]);
            if (description is null) reasons.Add(new DeskReason("pack.description", "must be a string of 1 to 200 characters"));
            if (pack["version"] is { } v && (version = Str(v)) is null) reasons.Add(new DeskReason("pack.version", "must be MAJOR.MINOR.PATCH"));
            if (pack["author"] is { } a && (author = Str(a)) is null) reasons.Add(new DeskReason("pack.author", "must be a string of 1 to 80 characters"));
            if (pack["license"] is { } l)
            {
                license = Str(l);
                if (license is null || !Licenses.Contains(license, StringComparer.Ordinal))
                    reasons.Add(new DeskReason("pack.license", "must be one of " + string.Join(", ", Licenses) + " (default " + DefaultLicense + ")"));
            }
        }

        // the kind's files
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        if (kind is not null && Kinds.Contains(kind, StringComparer.Ordinal))
        {
            foreach (var other in new[] { "sprite", "sheets", "palettes", "effects" })
            {
                bool belongs = (kind, other) is ("sprite", "sprite") or ("sprite", "sheets") or ("palette", "palettes") or ("effects", "effects");
                if (!belongs && args[other] is not null)
                    reasons.Add(new DeskReason(other, $"does not belong to a {kind} pack"));
            }
            switch (kind)
            {
                case "sprite":
                    BuildSprite(args, files, reasons);
                    break;
                case "palette":
                    BuildList(args["palettes"], "palettes", "palettes.json", files, reasons);
                    break;
                case "effects":
                    BuildList(args["effects"], "effects", "effects.json", files, reasons);
                    break;
            }
        }
        if (reasons.Count > 0) return new WidgetBuild(null, reasons);

        var manifest = new JsonObject
        {
            ["schema_version"] = 1,
            ["id"] = id,
            ["name"] = name,
            ["version"] = version,
            ["author"] = author,
            ["license"] = license,
            ["kind"] = kind,
            ["sanduhr_min"] = SanduhrMin,
            ["description"] = description,
        };
        var listed = new JsonArray();
        foreach (var rel in files.Keys) listed.Add((JsonNode?)rel);
        manifest["files"] = listed;
        files["pack.json"] = Utf8(manifest.ToJsonString(Indented) + "\n");
        files["LICENSE"] = Utf8(LicenseText(license!, author!, now.Year));

        return Check(files, id);
    }

    private static void BuildSprite(JsonObject args, SortedDictionary<string, byte[]> files, List<DeskReason> reasons)
    {
        if (args["sprite"] is not JsonObject sprite)
        {
            reasons.Add(new DeskReason("sprite", "must be an object: sprite.json (schema_version 2) with its stages or sheet, animations, moods, triggers and roam"));
            return;
        }
        var copy = (JsonObject)sprite.DeepClone();
        copy["schema_version"] ??= 2;
        foreach (var path in SheetPaths(copy))
        {
            if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                reasons.Add(new DeskReason("sprite", $"names the PNG sheet {Clip(path, 40)}; Claude never supplies an image. Draw it as a .json pixel sheet in sheets"));
        }
        files["sprite.json"] = Utf8(copy.ToJsonString(Indented) + "\n");

        if (args["sheets"] is not JsonObject sheets || sheets.Count == 0)
        {
            reasons.Add(new DeskReason("sheets", "must be an object of pixel sheets: path (ending .json) to {palette, frame_w, frame_h, frames}"));
            return;
        }
        if (sheets.Count > 6)
            reasons.Add(new DeskReason("sheets", "at most 6 pixel sheets (one per stage)"));
        foreach (var (path, sheet) in sheets)
        {
            if (!path.EndsWith(".json", StringComparison.Ordinal) || !PackValidator.SafeRelPath(path) || path == "sprite.json")
            {
                reasons.Add(new DeskReason("sheets", $"{Clip(path, 40)}: a sheet is a .json pixel sheet with a safe relative path (no PNG, no font)"));
                continue;
            }
            if (sheet is not JsonObject so)
            {
                reasons.Add(new DeskReason("sheets", $"{Clip(path, 40)}: must be an object {{palette, frame_w, frame_h, frames}}"));
                continue;
            }
            files[path] = Utf8(so.ToJsonString(Indented) + "\n");
        }
    }

    /// <summary>palettes.json or effects.json: the object as given, or a bare list wrapped as <c>{schema_version: 1, key: list}</c>.</summary>
    private static void BuildList(JsonNode? given, string key, string file, SortedDictionary<string, byte[]> files, List<DeskReason> reasons)
    {
        JsonObject doc;
        if (given is JsonArray list) doc = new JsonObject { ["schema_version"] = 1, [key] = list.DeepClone() };
        else if (given is JsonObject o)
        {
            doc = (JsonObject)o.DeepClone();
            doc["schema_version"] ??= 1;
        }
        else
        {
            reasons.Add(new DeskReason(key, $"must be {file}'s object {{schema_version: 1, {key}: [...]}} or the list alone"));
            return;
        }
        files[file] = Utf8(doc.ToJsonString(Indented) + "\n");
    }

    /// <summary>Every sheet path sprite.json names (top-level <c>sheet</c> and each stage's).</summary>
    private static IEnumerable<string> SheetPaths(JsonObject sprite)
    {
        if (Str(sprite["sheet"]) is { } single) yield return single;
        if (sprite["stages"] is JsonArray stages)
            foreach (var st in stages)
                if (st is JsonObject so && Str(so["sheet"]) is { } s) yield return s;
    }

    // ---- both halves: the rules on a built pack ----------------------------------------------------------------------

    /// <summary>
    /// The rules a Claude-made pack meets, on its files: JSON and the LICENSE only (no PNG, no font, nothing else), the id
    /// namespaced <c>my-</c> and equal to <paramref name="expectedId"/>, a kind Claude may propose, and <see cref="PackValidator"/>
    /// in strict mode (which carries the trademark rule). The server runs it before writing the request; the widget runs it again
    /// on the request.
    /// </summary>
    public static WidgetBuild Check(IReadOnlyDictionary<string, byte[]> files, string? expectedId)
    {
        var reasons = new List<DeskReason>();
        foreach (var rel in files.Keys.Order(StringComparer.Ordinal))
        {
            if (rel is "pack.json" or "LICENSE") continue;
            if (!rel.EndsWith(".json", StringComparison.Ordinal))
                reasons.Add(new DeskReason("files", $"{Clip(rel, 40)}: a Claude-made pack holds JSON only; no PNG, no font, nothing else"));
        }
        if (expectedId is null || !expectedId.StartsWith(IdPrefix, StringComparison.Ordinal))
            reasons.Add(new DeskReason("pack.id", "a Claude-made pack's id starts with my-"));
        if (reasons.Count > 0) return new WidgetBuild(null, reasons);

        var verdict = PackValidator.Validate(files, folderName: expectedId, strict: true);
        if (!verdict.Ok || verdict.Manifest is not { } manifest)
            return new WidgetBuild(null, [.. verdict.Errors.Take(24).Select(e => new DeskReason("pack", e))]);
        if (!Kinds.Contains(manifest.Kind, StringComparer.Ordinal))
            return new WidgetBuild(null, [new DeskReason("kind", "a Claude-made pack is a sprite, a palette or effects")]);
        return new WidgetBuild(new WidgetProposalPack(files, manifest), []);
    }

    // ---- the request file ----------------------------------------------------------------------------------------------

    /// <summary>The request file: <c>{schema_version, id, requested_at, pack_id, note?, files: {path: text}}</c>. Every file is
    /// text (JSON, or the LICENSE), so the request is data only.</summary>
    public static JsonObject RequestJson(string requestId, WidgetProposalPack pack, string? note, DateTimeOffset now)
    {
        var files = new JsonObject();
        foreach (var (rel, bytes) in pack.Files.OrderBy(f => f.Key, StringComparer.Ordinal))
            files[rel] = Encoding.UTF8.GetString(bytes);
        var request = new JsonObject
        {
            ["schema_version"] = SchemaVersion,
            ["id"] = requestId,
            ["requested_at"] = Stamp(now),
            ["pack_id"] = pack.Id,
        };
        if (!string.IsNullOrWhiteSpace(note)) request["note"] = note;
        request["files"] = files;
        return request;
    }

    /// <summary>
    /// Reads a request and checks the pack again with the widget's own rules. Unreadable, without an id, or older than
    /// <see cref="MaxAge"/> is dropped; a pack that breaks a rule is refused with the reasons; a good one is a proposal.
    /// </summary>
    public static WidgetRequest Decode(byte[] data, DateTimeOffset now)
    {
        if (data.Length > MaxRequestBytes) return WidgetRequest.Discard;
        JsonNode? root;
        try { root = JsonNode.Parse(data); }
        catch (JsonException) { return WidgetRequest.Discard; }
        if (root is not JsonObject o) return WidgetRequest.Discard;
        if (Str(o["id"]) is not { Length: > 0 and <= 64 } id) return WidgetRequest.Discard;
        if (Str(o["requested_at"]) is not { } stamp || ParseStamp(stamp) is not { } at)
            return Refused(id, new DeskReason("requested_at", "is missing or not a date"));
        if (now - at > MaxAge) return WidgetRequest.Discard;
        if (!(o["schema_version"] is JsonValue sv && sv.TryGetValue<int>(out var version) && version == SchemaVersion))
            return Refused(id, new DeskReason("schema_version", "this Sanduhr reads widget requests of schema 1"));
        string? packId = Str(o["pack_id"]);
        string? note = Str(o["note"]);
        if (note is { Length: > 200 }) note = note[..200];
        if (o["files"] is not JsonObject list || list.Count == 0 || list.Count > PackValidator.MaxFiles + 3)
            return Refused(id, new DeskReason("files", "must be an object of the pack's files"));
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (rel, value) in list)
        {
            if (Str(value) is not { } text)
                return Refused(id, new DeskReason("files", $"{Clip(rel, 40)}: a Claude-made pack's files are text (JSON or the LICENSE)"));
            files[rel] = Utf8(text);
        }
        var check = Check(files, packId);
        return check.Pack is { } pack
            ? new WidgetRequest(WidgetRequestKind.Proposal, id, pack, note, [])
            : new WidgetRequest(WidgetRequestKind.Refused, id, null, null, check.Reasons);
    }

    private static WidgetRequest Refused(string id, DeskReason reason) => new(WidgetRequestKind.Refused, id, null, null, [reason]);

    /// <summary>The result file's contents: <c>{completed_at, id, result: {status, reason?, reasons?, pack_id?, companion?}}</c>.</summary>
    public static byte[] ResultJson(string id, WidgetAnswer answer, DateTimeOffset now)
    {
        var result = new JsonObject { ["status"] = answer.Status };
        if (answer.Reason is not null) result["reason"] = answer.Reason;
        if (answer.Reasons is { Count: > 0 } reasons)
        {
            var arr = new JsonArray();
            foreach (var r in reasons) arr.Add((JsonNode?)r.ToJson());
            result["reasons"] = arr;
        }
        if (answer.PackId is not null) result["pack_id"] = answer.PackId;
        if (answer.Companion is { } c) result["companion"] = c;
        var root = new JsonObject
        {
            ["completed_at"] = Stamp(now),
            ["id"] = id,
            ["result"] = result,
        };
        return Utf8(root.ToJsonString());
    }

    // ---- LICENSE texts ---------------------------------------------------------------------------------------------------

    /// <summary>The LICENSE a Claude-made pack carries for <paramref name="spdx"/>. Each text holds the phrase the validator looks for.</summary>
    public static string LicenseText(string spdx, string author, int year)
    {
        string holder = $"Copyright (c) {year} {author}";
        return spdx switch
        {
            "MIT" =>
                $"MIT License\n\n{holder}\n\n"
                + "Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated "
                + "documentation files (the \"Software\"), to deal in the Software without restriction, including without limitation "
                + "the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to "
                + "permit persons to whom the Software is furnished to do so, subject to the following conditions:\n\n"
                + "The above copyright notice and this permission notice shall be included in all copies or substantial portions of the "
                + "Software.\n\n"
                + "THE SOFTWARE IS PROVIDED \"AS IS\", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE "
                + "WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR "
                + "COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR "
                + "OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.\n",
            "ISC" =>
                $"ISC License\n\n{holder}\n\n"
                + "Permission to use, copy, modify, and/or distribute this software for any purpose with or without fee is hereby "
                + "granted, provided that the above copyright notice and this permission notice appear in all copies.\n\n"
                + "THE SOFTWARE IS PROVIDED \"AS IS\" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH REGARD TO THIS SOFTWARE INCLUDING ALL "
                + "IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY SPECIAL, DIRECT, "
                + "INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN "
                + "AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR "
                + "PERFORMANCE OF THIS SOFTWARE.\n",
            "BSD-3-Clause" =>
                $"BSD 3-Clause License\n\n{holder}\n\n"
                + "Redistribution and use in source and binary forms, with or without modification, are permitted provided that the "
                + "following conditions are met:\n\n"
                + "1. Redistributions of source code must retain the above copyright notice, this list of conditions and the following "
                + "disclaimer.\n\n"
                + "2. Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following "
                + "disclaimer in the documentation and/or other materials provided with the distribution.\n\n"
                + "3. Neither the name of the copyright holder nor the names of its contributors may be used to endorse or promote "
                + "products derived from this software without specific prior written permission.\n\n"
                + "THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS \"AS IS\" AND ANY EXPRESS OR IMPLIED WARRANTIES, "
                + "INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE "
                + "DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, "
                + "SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF "
                + "THE POSSIBILITY OF SUCH DAMAGE.\n",
            "Apache-2.0" =>
                $"{holder}\n\n"
                + "Licensed under the Apache License, Version 2.0 (the \"License\"); you may not use this file except in compliance "
                + "with the License. You may obtain a copy of the License at\n\n    http://www.apache.org/licenses/LICENSE-2.0\n\n"
                + "Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on "
                + "an \"AS IS\" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the "
                + "specific language governing permissions and limitations under the License.\n",
            "CC-BY-4.0" =>
                $"{holder}\n\n"
                + "This work is licensed under the Creative Commons Attribution 4.0 International License (CC BY 4.0). You are free "
                + "to share and adapt it for any purpose, provided you give appropriate credit, link to the license and indicate if "
                + "changes were made.\n\nThe full license text: https://creativecommons.org/licenses/by/4.0/legalcode\n",
            _ =>
                $"CC0 1.0 Universal\n\n{holder}\n\n"
                + "To the extent possible under law, the author has waived all copyright and related or neighboring rights to this "
                + "work, dedicating it to the public domain worldwide. This work is distributed without any warranty.\n\n"
                + "The full legal code: https://creativecommons.org/publicdomain/zero/1.0/legalcode\n",
        };
    }

    // ---- helpers ---------------------------------------------------------------------------------------------------------

    private static string? Str(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max] + "...";

    public static string Stamp(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseStamp(string s) =>
        DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t) ? t : null;
}
