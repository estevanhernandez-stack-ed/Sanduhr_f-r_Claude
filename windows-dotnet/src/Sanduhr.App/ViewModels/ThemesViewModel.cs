using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Services;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// Backs the Settings → Themes tab — the agent-assisted theme-creation surface
/// ported from <c>settings_dialog._build_themes_tab</c>. Paste a theme JSON (or
/// what an AI agent returned), save it to <c>%APPDATA%\Sanduhr\themes</c>, and
/// apply it live; copy the embedded agent prompt to the clipboard; open the
/// themes folder; and manage the installed user themes (Apply / Reload / Delete).
///
/// All theme application is delegated to the <see cref="WidgetViewModel"/> (the
/// single owner of the live palette + strip), so a save/apply here re-tints the
/// widget at once. Chimes replace the OS dialog beep: success on save/apply,
/// error on validation failure, info on copy — parity with where Python plays them.
/// </summary>
public sealed partial class ThemesViewModel : ObservableObject
{
    private static readonly Regex NonAlnum = new("[^a-z0-9]+", RegexOptions.Compiled);

    private readonly WidgetViewModel _widget;
    private Window? _owner;

    [ObservableProperty] private string _pasteJson = "";
    [ObservableProperty] private string _fileName = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplySelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    private string? _selectedTheme;

    /// <summary>The *.json file names installed in the themes folder.</summary>
    public ObservableCollection<string> InstalledThemes { get; } = new();

    /// <summary>True when no user themes are installed — drives the list's empty hint.</summary>
    [ObservableProperty] private bool _hasNoThemes;

    public ThemesViewModel(WidgetViewModel widget)
    {
        _widget = widget;
        RefreshList();
    }

    /// <summary>Modal message boxes parent to the Settings window.</summary>
    public void AttachOwner(Window owner) => _owner = owner;

    /// <summary>Auto-fill the filename (lowercase-kebab) from the pasted JSON's
    /// "name" — but only while the user hasn't typed one. Parity with
    /// <c>settings_dialog._autofill_filename</c>.</summary>
    partial void OnPasteJsonChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(FileName))
            return;
        try
        {
            if (JsonNode.Parse(value) is JsonObject obj &&
                obj["name"]?.GetValue<string>() is { Length: > 0 } name)
            {
                FileName = Slugify(name);
            }
        }
        catch (JsonException)
        {
            // Incomplete / invalid JSON while typing — leave the field alone.
        }
    }

    [RelayCommand]
    private void SaveAndApply()
    {
        var raw = PasteJson.Trim();
        if (raw.Length == 0)
        {
            Error("Paste a theme JSON first.");
            return;
        }

        JsonObject? data;
        try
        {
            data = JsonNode.Parse(raw) as JsonObject;
        }
        catch (JsonException e)
        {
            Error($"Invalid JSON: {e.Message}");
            return;
        }
        if (data is null)
        {
            Error("Theme JSON must be an object.");
            return;
        }

        var filename = FileName.Trim();
        if (filename.Length == 0 && data["name"]?.GetValue<string>() is { Length: > 0 } name)
            filename = Slugify(name);
        if (filename.Length == 0)
        {
            Error("Filename is required.");
            return;
        }
        if (!filename.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            filename += ".json";

        var dir = _widget.ThemesDir;
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(
                Path.Combine(dir, filename),
                data.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (IOException e)
        {
            Error($"Could not write theme: {e.Message}");
            return;
        }

        _widget.ReloadUserThemes();
        RefreshList();

        var key = Path.GetFileNameWithoutExtension(filename).ToLowerInvariant();
        if (_widget.HasTheme(key))
        {
            _widget.ApplyThemeByKey(key);
            Sounds.PlaySaveConfirmation();
            Info($"Saved and applied: {filename}");
        }
        else
        {
            // Saved, but it didn't pass validation (missing required fields), so
            // it can't be applied — surface it rather than silently swallow.
            Sounds.PlayError();
            Info($"Saved {filename}, but it's missing required fields and wasn't applied. " +
                 "Check the JSON against Copy agent prompt.");
        }

        PasteJson = "";
        FileName = "";
    }

    [RelayCommand]
    private void CopyAgentPrompt()
    {
        var text = AgentPrompt.Load();
        try
        {
            Clipboard.SetText(text);
        }
        catch
        {
            // Clipboard can be transiently locked by another app — soft-fail.
        }
        Sounds.PlayInfo();
        Info("Agent prompt copied to clipboard.\n\n" +
             "Paste it into any chat agent with a reference image or vibe " +
             "description, then drop the returned JSON into the paste box above.");
    }

    [RelayCommand]
    private void OpenThemesFolder()
    {
        var dir = _widget.ThemesDir;
        try
        {
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
        }
        catch
        {
            Error("Could not open the themes folder.");
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ApplySelected()
    {
        if (SelectedTheme is null)
        {
            Error("Pick a theme from the list first.");
            return;
        }
        var key = Path.GetFileNameWithoutExtension(SelectedTheme).ToLowerInvariant();
        if (!_widget.HasTheme(key))
        {
            Error($"Theme '{key}' isn't loaded — try Reload first.");
            return;
        }
        _widget.ApplyThemeByKey(key);
        Sounds.PlaySaveConfirmation();
    }

    [RelayCommand]
    private void Reload()
    {
        _widget.ReloadUserThemes();
        RefreshList();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete()
    {
        if (SelectedTheme is null)
            return;
        var name = SelectedTheme;
        var result = _owner is not null
            ? MessageBox.Show(_owner, $"Delete {name}?", "Themes",
                MessageBoxButton.YesNo, MessageBoxImage.None)
            : MessageBox.Show($"Delete {name}?", "Themes",
                MessageBoxButton.YesNo, MessageBoxImage.None);
        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            File.Delete(Path.Combine(_widget.ThemesDir, name));
        }
        catch (IOException e)
        {
            Error($"Could not delete: {e.Message}");
            return;
        }
        _widget.ReloadUserThemes();
        RefreshList();
    }

    private bool HasSelection() => SelectedTheme is not null;

    private void RefreshList()
    {
        InstalledThemes.Clear();
        var dir = _widget.ThemesDir;
        if (!Directory.Exists(dir))
        {
            HasNoThemes = true;
            return;
        }
        var files = Directory.GetFiles(dir, "*.json");
        Array.Sort(files, StringComparer.Ordinal);
        foreach (var path in files)
            InstalledThemes.Add(Path.GetFileName(path));
        HasNoThemes = InstalledThemes.Count == 0;
    }

    /// <summary>"Sunset Neon" → "sunset-neon" (settings_dialog._slugify).</summary>
    private static string Slugify(string name)
    {
        var s = NonAlnum.Replace(name.Trim().ToLowerInvariant(), "-").Trim('-');
        return s.Length == 0 ? "theme" : s;
    }

    private void Error(string message)
    {
        Sounds.PlayError();
        Show(message);
    }

    private void Info(string message) => Show(message);

    private void Show(string message)
    {
        // MessageBoxImage.None suppresses the OS beep — our chime already played.
        if (_owner is not null)
            MessageBox.Show(_owner, message, "Themes", MessageBoxButton.OK, MessageBoxImage.None);
        else
            MessageBox.Show(message, "Themes", MessageBoxButton.OK, MessageBoxImage.None);
    }
}
