using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>
/// What the widget does with a <see cref="ThemeHandoff.Request"/>, minus the
/// WPF part: lint (the widget is the authority even though the server linted),
/// resolve the file key, refuse a built-in's name, write the theme under the
/// themes folder, and build the typed result. The caller applies the theme
/// when <see cref="Outcome.ApplyKey"/> is set and writes the result file.
/// Pure enough to test with a temp folder.
/// </summary>
public static class ThemeProposals
{
    /// <summary><paramref name="Payload"/> is the typed tool result;
    /// <paramref name="ApplyKey"/> is the key to apply, null when the request
    /// said not to or the theme was refused.</summary>
    public sealed record Outcome(JsonObject Payload, string? ApplyKey, string? SavedPath);

    public static Outcome Process(ThemeHandoff.Request request, string themesDir, string activeKey, Func<string, bool> isReservedKey)
    {
        var lint = ThemeLint.Lint(request.Theme);
        if (!lint.Ok)
        {
            var rejected = Typed("rejected", "invalid_theme", "Fix the fields named in findings and call again.");
            rejected["findings"] = ThemeLint.ToJson(lint.Findings);
            return new Outcome(rejected, null, null);
        }

        string name = lint.Theme!.Name;
        string key = request.SaveAs ?? ThemeHandoff.Slugify(name);
        if (!ThemeHandoff.IsValidKey(key))
            return new Outcome(Typed("rejected", "invalid_params", "save_as must be 1-40 lowercase letters, digits or hyphens."), null, null);
        if (isReservedKey(key))
            return new Outcome(Typed("rejected", "reserved_name", $"'{key}' is a built-in theme; pick another name or pass save_as."), null, null);

        string path = Path.Combine(themesDir, key + ".json");
        try
        {
            Directory.CreateDirectory(themesDir);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, request.Theme.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Outcome(Typed("error", "save_failed", $"Could not write the theme file ({e.GetType().Name})."), null, null);
        }

        var payload = Typed(request.Apply ? "applied" : "saved", null, null);
        payload["key"] = key;
        payload["name"] = name;
        payload["previous_key"] = activeKey;
        payload["saved_path"] = path;
        payload["findings"] = ThemeLint.ToJson(lint.Findings);
        return new Outcome(payload, request.Apply ? key : null, path);
    }

    private static JsonObject Typed(string status, string? reason, string? remedy) => new()
    {
        ["status"] = status,
        ["reason"] = reason,
        ["remedy"] = remedy,
    };
}
