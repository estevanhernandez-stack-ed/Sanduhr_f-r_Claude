using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// One row of the sidebar's search results: a page, or a section or control under it. <paramref name="CollapsedIn"/>
/// names the collapsed section it sits in, so the row says "(in Advanced)" and opening it unfolds the section.
/// </summary>
public sealed record SettingsSearchRow(string Title, SettingsPage Page, string? Anchor, bool IsPage, string? CollapsedIn = null)
{
    /// <summary>The row's words: the title, with "(in Advanced)" when it sits in a collapsed section.</summary>
    public string DisplayTitle => CollapsedIn is null ? Title : SettingsNames.InSection(Title, CollapsedIn);

    /// <summary>
    /// What a screen reader says: the page alone, "Tray shows, on General", or "Margins, in Advanced, on Desk"
    /// (no parentheses when spoken).
    /// </summary>
    public string AutomationName => IsPage
        ? $"{Title} page"
        : $"{(CollapsedIn is null ? Title : SettingsNames.InSectionSpoken(Title, CollapsedIn))}, on {SettingsNames.PageTitle(Page)}";
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
        var hits = SettingsIndex.Search(value);
        var best = hits.Count > 0 ? hits[0].Entry : null;
        _first = null;
        foreach (var (page, entries) in SettingsIndex.Grouped(hits))
        {
            var pageRow = new SettingsSearchRow(SettingsNames.PageTitle(page), page, null, IsPage: true);
            Results.Add(pageRow);
            if (best is not null && best.Page == page && best.Anchor is null)
                _first ??= pageRow;
            foreach (var e in entries)
            {
                var row = new SettingsSearchRow(e.Title, page, e.Anchor, IsPage: false, e.CollapsedIn);
                Results.Add(row);
                if (best is not null && best.Page == page && best.Anchor is not null && best.Title == e.Title)
                    _first ??= row;
            }
        }
        OnPropertyChanged(nameof(IsSearching));
        OnPropertyChanged(nameof(HasNoResults));
    }

    private SettingsSearchRow? _first;

    /// <summary>The row Enter opens from the search box: the best hit, a page or a section
    /// ("publish" opens Publish usage, "vault" opens Session archive).</summary>
    public SettingsSearchRow? First => _first ?? Results.FirstOrDefault();

    public void Clear() => Query = "";
}
