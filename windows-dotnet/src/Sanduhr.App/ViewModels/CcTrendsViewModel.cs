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

    public CcTrendsViewModel(WidgetViewModel widget)
    {
        _widget = widget;
        _widget.ThemeChanged += _ => Changed?.Invoke();
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
        TopProjects.Clear();
        foreach (var (name, total) in data.Top)
            TopProjects.Add(new BreakdownRow(name, TokenFormat.Compact(total)));

        var todayLocal = DateOnly.FromDateTime(DateTime.Now);
        FooterText = data.Birth is { } birth
            ? $"history preserved since {birth.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture)}"
            : "";
        InfoText = data.Birth is { } b && todayLocal.DayNumber - b.DayNumber <= 1
            ? "Fresh vault — the first backfill seeded about 4 weeks from your existing logs; longer trends fill in from here."
            : "";
        Changed?.Invoke();
    }
}
