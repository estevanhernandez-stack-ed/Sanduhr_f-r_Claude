using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>One color token row: the swatch, the hex as typed, and whether it parses.</summary>
public sealed partial class ThemeTokenRowViewModel : ObservableObject
{
    private readonly ThemeStudioViewModel _owner;
    private bool _loading;

    public string Field { get; }
    public string Label { get; }
    public string Job { get; }

    [ObservableProperty] private string _hex = "";
    [ObservableProperty] private Brush _swatch = Brushes.Transparent;
    [ObservableProperty] private bool _hasError;

    public ThemeTokenRowViewModel(ThemeStudioViewModel owner, string field, string label, string job)
    {
        _owner = owner;
        Field = field;
        Label = label;
        Job = job;
    }

    /// <summary>Load a value without treating it as a user edit.</summary>
    internal void Load(string hex, (double R, double G, double B)? lastGood)
    {
        _loading = true;
        Hex = hex;
        HasError = !ThemeLint.TryParseHex(hex, out _);
        Swatch = lastGood is { } c ? Frozen(c) : Brushes.Transparent;
        _loading = false;
    }

    partial void OnHexChanged(string value)
    {
        if (_loading)
            return;
        bool ok = _owner.OnTokenEdited(Field, value);
        HasError = !ok;
        if (ok && ThemeLint.TryParseHex(value.Trim(), out var c))
            Swatch = Frozen(c);
    }

    private static SolidColorBrush Frozen((double R, double G, double B) c)
    {
        var b = new SolidColorBrush(Color.FromRgb((byte)Math.Round(c.R * 255), (byte)Math.Round(c.G * 255), (byte)Math.Round(c.B * 255)));
        b.Freeze();
        return b;
    }
}

/// <summary>A lint finding for the list under the rows.</summary>
public sealed record ThemeFindingViewModel(string Field, string Message, bool IsError);

/// <summary>
/// The Theme Studio: token-level editing with the widget itself as the live
/// preview. Every valid edit re-tints the widget through
/// <see cref="WidgetViewModel.PreviewTheme"/> without changing the saved theme;
/// Revert, Save, switching back to Paste mode, or closing Settings ends the
/// preview and the saved theme is the truth again. The state lives in a Core
/// <see cref="ThemeDraft"/> (tested there); this class adds brushes, commands
/// and the findings list.
/// </summary>
public sealed partial class ThemeStudioViewModel : ObservableObject
{
    private readonly WidgetViewModel _widget;
    private readonly Action<string> _error;
    private readonly Action<string> _info;
    private readonly Action _saved;
    private ThemeDraft _draft = new();
    private bool _loading;

    public ObservableCollection<ThemeTokenRowViewModel> Rows { get; } = new();
    public ObservableCollection<ThemeFindingViewModel> Findings { get; } = new();

    /// <summary>The strip's themes (built-ins then user themes), for "Start from".</summary>
    public ObservableCollection<ThemeStripItemViewModel> StartFromOptions => _widget.Themes;

    [ObservableProperty] private ThemeStripItemViewModel? _selectedStart;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private double _glassAlpha = 0.80;
    [ObservableProperty] private double _borderAlpha = 0.40;
    [ObservableProperty] private string _fileName = "";
    [ObservableProperty] private bool _isValid;
    [ObservableProperty] private bool _hasFindings;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _isPreviewing;

    public string GlassAlphaText => GlassAlpha.ToString("0.00");
    public string BorderAlphaText => BorderAlpha.ToString("0.00");

    public ThemeStudioViewModel(WidgetViewModel widget, Action<string> error, Action<string> info, Action saved)
    {
        _widget = widget;
        _error = error;
        _info = info;
        _saved = saved;
        foreach (var (field, label, job) in ThemeDraft.ColorTokens)
            Rows.Add(new ThemeTokenRowViewModel(this, field, label, job));
    }

    /// <summary>Called when the studio becomes visible: start from the active
    /// theme unless a draft is already in progress.</summary>
    public void Activate()
    {
        if (_draft.SourceKey is null && Rows.All(r => r.Hex.Length == 0))
            LoadFrom(_widget.ActiveThemeKey);
    }

    /// <summary>Called when the studio goes away (mode switch, Settings closed):
    /// the saved theme comes back, the draft stays for next time.</summary>
    public void Deactivate() => EndPreview();

    partial void OnSelectedStartChanged(ThemeStripItemViewModel? value)
    {
        if (_loading || value is null)
            return;
        LoadFrom(value.Key);
    }

    private void LoadFrom(string key)
    {
        var def = _widget.GetThemeDefinition(key);
        if (def is null)
            return;
        _loading = true;
        _draft = ThemeDraft.FromDefinition(key, def);
        Name = _draft.Name;
        GlassAlpha = _draft.GlassAlpha;
        BorderAlpha = _draft.BorderAlpha;
        FileName = ThemeCatalog.BuiltIns.ContainsKey(key) ? "" : key;
        foreach (var row in Rows)
            row.Load(_draft.GetColor(row.Field), _draft.TryGetColor(row.Field));
        SelectedStart = StartFromOptions.FirstOrDefault(t => t.Key == key);
        _loading = false;
        Refresh(preview: false);
    }

    internal bool OnTokenEdited(string field, string hex)
    {
        bool ok = _draft.SetColor(field, hex);
        Refresh(preview: true);
        return ok;
    }

    partial void OnNameChanged(string value)
    {
        if (_loading) return;
        _draft.Name = value;
        Refresh(preview: true);
    }

    partial void OnGlassAlphaChanged(double value)
    {
        OnPropertyChanged(nameof(GlassAlphaText));
        if (_loading) return;
        _draft.GlassAlpha = value;
        Refresh(preview: true);
    }

    partial void OnBorderAlphaChanged(double value)
    {
        OnPropertyChanged(nameof(BorderAlphaText));
        if (_loading) return;
        _draft.BorderAlpha = value;
        Refresh(preview: true);
    }

    /// <summary>Re-lint, refresh the findings list, and (for user edits) push the
    /// draft into the widget as the live preview.</summary>
    private void Refresh(bool preview)
    {
        var lint = _draft.Lint();
        Findings.Clear();
        foreach (var f in lint.Findings.OrderBy(f => f.Level == ThemeFindingLevel.Error ? 0 : 1))
            Findings.Add(new ThemeFindingViewModel(f.Field, f.Message, f.Level == ThemeFindingLevel.Error));
        IsValid = lint.Ok;
        HasFindings = Findings.Count > 0;
        int errors = lint.Errors.Count(), warnings = lint.Warnings.Count();
        Summary = errors > 0 ? $"{errors} to fix before saving"
            : warnings > 0 ? $"Saveable, {warnings} design {(warnings == 1 ? "note" : "notes")}"
            : "Looks good";

        if (!preview)
            return;
        if (_draft.PreviewDefinition() is { } def)
        {
            _widget.PreviewTheme(def);
            IsPreviewing = true;
        }
    }

    [RelayCommand]
    private void Save()
    {
        var lint = _draft.Lint();
        if (!lint.Ok)
        {
            _error("Fix the errors listed under the rows first: " + string.Join("; ", lint.Errors.Select(e => e.Field)));
            return;
        }
        string key = FileName.Trim().Length > 0 ? FileName.Trim().ToLowerInvariant() : ThemeHandoff.Slugify(Name);
        if (key.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            key = key[..^5];
        if (!ThemeHandoff.IsValidKey(key))
        {
            _error("The file name must be 1-40 lowercase letters, digits or hyphens.");
            return;
        }
        if (ThemeCatalog.BuiltIns.ContainsKey(key))
        {
            _error($"'{key}' is a built-in theme. Pick another file name; the built-ins stay as they ship.");
            return;
        }

        string dir = _widget.ThemesDir;
        string path = Path.Combine(dir, key + ".json");
        try
        {
            Directory.CreateDirectory(dir);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, _draft.ToJsonText());
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _error($"Could not write the theme: {e.Message}");
            return;
        }

        _widget.ReloadUserThemes();
        _widget.ApplyThemeByKey(key);   // ends the preview: the saved theme is the truth again
        IsPreviewing = false;
        FileName = key;
        _draft = ThemeDraft.FromDefinition(key, _widget.GetThemeDefinition(key)!);
        _loading = true;
        SelectedStart = StartFromOptions.FirstOrDefault(t => t.Key == key);
        _loading = false;
        Sounds.PlaySaveConfirmation();
        _saved();
        int warnings = lint.Warnings.Count();
        _info(warnings == 0
            ? $"Saved and applied: {key}.json"
            : $"Saved and applied: {key}.json ({warnings} design {(warnings == 1 ? "note" : "notes")} listed under the rows).");
    }

    [RelayCommand]
    private void Revert()
    {
        EndPreview();
        LoadFrom(_widget.ActiveThemeKey);
    }

    [RelayCommand]
    private void CopyJson()
    {
        try
        {
            Clipboard.SetText(_draft.ToJsonText());
        }
        catch
        {
            // Clipboard can be transiently locked by another app; soft-fail.
        }
        Sounds.PlayInfo();
        _info("Theme JSON copied. Hand it to an agent with what you want changed, and paste the answer back.");
    }

    private void EndPreview()
    {
        if (!IsPreviewing)
            return;
        _widget.EndPreview();
        IsPreviewing = false;
    }
}
