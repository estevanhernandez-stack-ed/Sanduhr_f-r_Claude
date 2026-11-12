using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// One usage source on the widget (agent layer): Claude, then Codex, each a section with its own cards. With one source ticked the
/// header is hidden and the widget looks as it did before sources existed. A section can collapse to one line
/// ("5h 40% · wk 12%"); the widget saves that through <see cref="CollapsedChanged"/>.
/// </summary>
public sealed partial class SourceSectionViewModel : ObservableObject
{
    public const string NoLimitsLine = "Run Codex once to see your limits.";
    public const string UnreadableLine = "Couldn't read Codex's logs.";

    public SourceSectionViewModel(string source, Brush color, bool collapsed)
    {
        Source = source;
        Name = AgentSources.Name(source);
        _color = color;
        _collapsed = collapsed;
    }

    public string Source { get; }

    public string Name { get; }

    public ObservableCollection<TierCardViewModel> Tiers { get; } = new();

    [ObservableProperty] private Brush _color;

    [ObservableProperty] private bool _collapsed;

    /// <summary>"as of 3:42 PM" (with the date past 7 days) while the reading is not live; empty otherwise.</summary>
    [ObservableProperty] private string _asOfText = "";

    /// <summary>The whole reading is stale (no tier fresh): the header's "as of" reads dim.</summary>
    [ObservableProperty] private bool _stale;

    /// <summary>The line shown instead of cards when the source has no reading, or null.</summary>
    [ObservableProperty] private string? _emptyText;

    /// <summary>"5h 40% · wk 12%", shown while collapsed.</summary>
    [ObservableProperty] private string _collapsedSummary = "";

    /// <summary>The header row: with several sources always; alone, only a non-Claude section keeps it (name, as-of, chevron), so the
    /// Claude-only widget reads exactly as it always did.</summary>
    [ObservableProperty] private bool _showHeader;

    /// <summary>A lone Claude section has no header to expand it from, so it is always drawn expanded whatever was saved.</summary>
    [ObservableProperty] private bool _alwaysExpanded;

    /// <summary>The label Codex's cards and toasts use: Session (5hr) and Weekly.</summary>
    public static string CodexTierLabel(string tierKey) => tierKey switch
    {
        TierModel.FiveHour => "Session (5hr)",
        TierModel.SevenDay => "Weekly",
        _ => TierModel.Label(tierKey),
    };

    private bool ShownCollapsed => Collapsed && !AlwaysExpanded;

    /// <summary>Raised when the chevron toggles <see cref="Collapsed"/>; the widget saves it.</summary>
    public event Action<SourceSectionViewModel>? CollapsedChanged;

    public string CollapseName => (Collapsed ? "Expand " : "Collapse ") + Name;

    public bool ShowCards => !ShownCollapsed && EmptyText is null;

    public bool ShowEmpty => !ShownCollapsed && EmptyText is not null;

    public bool ShowSummary => ShownCollapsed;

    /// <summary>What a screen reader reads for the header's text: the source, then when it was read.</summary>
    public string HeaderName => AsOfText.Length == 0 ? Name : $"{Name}, {AsOfText}";

    /// <summary>The section's list item reads as its name, never as the type behind it.</summary>
    public override string ToString() => HeaderName;

    [RelayCommand]
    private void ToggleCollapse()
    {
        Collapsed = !Collapsed;
        CollapsedChanged?.Invoke(this);
    }

    /// <summary>Sets the collapsed state without raising <see cref="CollapsedChanged"/> (loading from settings).</summary>
    internal void SetCollapsed(bool collapsed) => Collapsed = collapsed;

    partial void OnCollapsedChanged(bool value)
    {
        OnPropertyChanged(nameof(CollapseName));
        OnPropertyChanged(nameof(ShowCards));
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(ShowSummary));
    }

    partial void OnAlwaysExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowCards));
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(ShowSummary));
    }

    partial void OnEmptyTextChanged(string? value)
    {
        OnPropertyChanged(nameof(ShowCards));
        OnPropertyChanged(nameof(ShowEmpty));
    }

    partial void OnAsOfTextChanged(string value) => OnPropertyChanged(nameof(HeaderName));
}
