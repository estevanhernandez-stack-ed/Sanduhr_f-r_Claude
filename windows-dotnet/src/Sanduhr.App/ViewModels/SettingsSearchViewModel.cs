using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>One row of the sidebar's search results: a page, or a section or control under it.</summary>
public sealed record SettingsSearchRow(string Title, SettingsPage Page, string? Anchor, bool IsPage)
{
    /// <summary>What a screen reader says: the page alone, or "Tray shows, on General".</summary>
    public string AutomationName => IsPage ? $"{Title} page" : $"{Title}, on {SettingsNames.PageTitle(Page)}";
}

/// <summary>
/// The sidebar search (Settings v2): typing filters to the pages, sections and controls whose name or
/// synonyms match, grouped by page in the order of their best hit (<see cref="SettingsIndex.Search"/>).
/// </summary>
public sealed partial class SettingsSearchViewModel : ObservableObject
{
    public ObservableCollection<SettingsSearchRow> Results { get; } = new();

    [ObservableProperty] private string _query = "";

    /// <summary>True while there is a query: the results list replaces the page list.</summary>
    public bool IsSearching => !string.IsNullOrWhiteSpace(Query);

    /// <summary>A query that found nothing.</summary>
    public bool HasNoResults => IsSearching && Results.Count == 0;

    partial void OnQueryChanged(string value)
    {
        Results.Clear();
        foreach (var (page, entries) in SettingsIndex.Grouped(SettingsIndex.Search(value)))
        {
            Results.Add(new SettingsSearchRow(SettingsNames.PageTitle(page), page, null, IsPage: true));
            foreach (var e in entries)
                Results.Add(new SettingsSearchRow(e.Title, page, e.Anchor, IsPage: false));
        }
        OnPropertyChanged(nameof(IsSearching));
        OnPropertyChanged(nameof(HasNoResults));
    }

    /// <summary>The row Enter opens from the search box: the best hit's first section, else its page.</summary>
    public SettingsSearchRow? First => Results.Count > 1 && !Results[1].IsPage ? Results[1] : Results.FirstOrDefault();

    public void Clear() => Query = "";
}
