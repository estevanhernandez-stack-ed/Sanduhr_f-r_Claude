using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Theming;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// Trends section: weekly total bars over 4/12/26 weeks from vault rollups,
/// top projects for the window, the vault-birth footer, and the day-1 backfill
/// note. All reads off-thread; the window re-pushes into CcTrendsControl on
/// <see cref="Changed"/> (custom-render controls can't bind).
/// </summary>
public sealed partial class CcTrendsViewModel : ObservableObject
{
    private readonly WidgetViewModel _widget;

    public event Action? Changed;

    [ObservableProperty] private int _weeksBack = 12;
    [ObservableProperty] private string _footerText = "";
    [ObservableProperty] private string _infoText = "";

    public ObservableCollection<BreakdownRow> TopProjects { get; } = new();

    private IReadOnlyList<VaultWeek> _weeks = Array.Empty<VaultWeek>();
    public IReadOnlyList<VaultWeek> Weeks => _weeks;

    public ThemePalette Palette => _widget.Palette;

    private void OnThemeChanged(ThemePalette _) => Changed?.Invoke();

    /// <summary>Stop following the widget's theme (Settings closed): the widget outlives the window.</summary>
    public void Detach() => _widget.ThemeChanged -= OnThemeChanged;

    public CcTrendsViewModel(WidgetViewModel widget)
    {
        _widget = widget;
        _widget.ThemeChanged += OnThemeChanged;
    }

    [RelayCommand]
    private async Task SetWeeks(string weeks)
    {
        if (int.TryParse(weeks, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            && n is 4 or 12 or 26)
        {
            WeeksBack = n;
            await RefreshAsync();
        }
    }

    private IReadOnlyList<(string Name, long Total)> _top = [];

    /// <summary>Trends' top projects (slice 7, CP 16): the same codenames as Overview's (one key, the project's name).</summary>
    internal static IReadOnlyList<BreakdownRow> Rows(IEnumerable<(string Name, long Total)> top) =>
        LocalCcViewModel.Rows(top, CodenameCategory.Project);

    private void FillTop()
    {
        TopProjects.Clear();
        foreach (var row in Rows(_top))
            TopProjects.Add(row);
    }

    /// <summary>Streamer mode moved (slice 7): the table reads its names again.</summary>
    public void RefreshNames() => FillTop();

    /// <summary>Tests fill the table as a refresh of the archive's top projects would.</summary>
    internal void ShowTopForTests(IEnumerable<(string Name, long Total)> top)
    {
        _top = [.. top];
        FillTop();
    }

    private sealed record TrendsData(
        IReadOnlyList<VaultWeek> Weeks,
        IReadOnlyList<(string Name, long Total)> Top,
        DateOnly? Birth);

    public async Task RefreshAsync()
    {
        var vault = _widget.Vault;
        int weeksBack = WeeksBack;
        TrendsData data;
        try
        {
            data = await Task.Run(() =>
            {
                if (vault is null)
                    return new TrendsData(Array.Empty<VaultWeek>(),
                        Array.Empty<(string, long)>(), null);
                var roots = vault.ConsentedRootNames();
                var today = DateOnly.FromDateTime(DateTime.Now);
                var weeks = vault.Reader.ReadWeeks(roots, weeksBack, today);
                var from = weeks.Count > 0 ? weeks[0].WeekStart : today;
                var top = vault.Reader.TopProjects(roots, from, today.AddDays(1), 5);
                return new TrendsData(weeks, top, vault.Reader.BirthDate(roots));
            }).ConfigureAwait(true);
        }
        catch
        {
            data = new TrendsData(Array.Empty<VaultWeek>(), Array.Empty<(string, long)>(), null);
        }

        _weeks = data.Weeks;
        _top = data.Top;   // streamer: masked - via FillTop: the top list draws codenames (CP 16)
        FillTop();

        var todayLocal = DateOnly.FromDateTime(DateTime.Now);
        FooterText = data.Birth is { } birth
            ? $"history preserved since {birth.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture)}"
            : "";
        InfoText = data.Birth is { } b && todayLocal.DayNumber - b.DayNumber <= 1
            ? "Fresh session archive: the first backfill seeded about 4 weeks from your existing logs; longer trends fill in from here."
            : "";
        Changed?.Invoke();
    }
}
