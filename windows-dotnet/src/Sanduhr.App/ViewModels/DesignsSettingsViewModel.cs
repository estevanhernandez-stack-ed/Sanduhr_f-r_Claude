using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Desk;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>One saved design in Settings, Desk, Designs.</summary>
public sealed class DesignRow
{
    public DesignRow(SavedDesign design)
    {
        Id = design.Id;
        Name = design.Name;
        Pinned = design.Pinned;
        Date = design.AppliedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    }

    public string Id { get; }
    public string Name { get; }
    public bool Pinned { get; }

    /// <summary>When it was applied, in the PC's own format.</summary>
    public string Date { get; }

    /// <summary>"Pin" or "Unpin", the button's words.</summary>
    public string PinText => Pinned ? "Unpin" : "Pin";

    /// <summary>The line the row reads out: its name, whether it is pinned and when it was applied.</summary>
    public string Summary => $"{Name}, {(Pinned ? "pinned, " : "")}applied {Date}";

    public string SwitchName => $"Switch to {Name}";
    public string RenameName => $"Rename {Name}";
    public string PinName => $"{PinText} {Name}";
    public string DeleteName => $"Delete {Name}";
}

/// <summary>
/// Settings, Desk, Designs (spec, the agent-designed Desk, saved designs): the designs kept, newest first, each with Switch to, Rename,
/// Pin or Unpin, and Delete, and <b>Put my Desk back</b>. Ten are kept; a pinned design is never dropped. Each action says what it
/// did in a polite live line.
/// </summary>
public sealed partial class DesignsSettingsViewModel : ObservableObject
{
    private readonly Func<DeskDesignService?> _service;
    private readonly Func<string, string, string, string?> _prompt;

    /// <param name="service">The app's design service (null where there is none: the list is empty).</param>
    /// <param name="prompt">Asks for a name: title, prompt, initial; null for Cancel. The default opens the small text prompt.</param>
    internal DesignsSettingsViewModel(Func<DeskDesignService?> service, Func<string, string, string, string?>? prompt = null)
    {
        _service = service;
        _prompt = prompt ?? DefaultPrompt;
        Reload();
    }

    public ObservableCollection<DesignRow> Rows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _status = "";

    public bool HasStatus => Status.Length > 0;

    public bool HasNoDesigns => Rows.Count == 0;

    public bool CanPutBack => _service()?.CanPutBack == true;

    /// <summary>"3 designs kept", plus the rule.</summary>
    public string CountText => Rows.Count switch
    {
        0 => "",
        1 => "1 design kept. ",
        var n => $"{n} designs kept. ",
    };

    /// <summary>Reads the list again (after any change, and when the service says it changed).</summary>
    public void Reload()
    {
        Rows.Clear();
        if (_service() is { } service)
            foreach (var d in service.Designs()) Rows.Add(new DesignRow(d));
        OnPropertyChanged(nameof(HasNoDesigns));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(CanPutBack));
        PutBackCommand.NotifyCanExecuteChanged();
    }

    private void Done(DesignActionResult result)
    {
        Status = result.Message;
        Reload();
    }

    [RelayCommand]
    private void SwitchTo(DesignRow? row)
    {
        if (row is null || _service() is not { } s) return;
        Done(s.SwitchTo(row.Id));
    }

    [RelayCommand]
    private void Rename(DesignRow? row)
    {
        if (row is null || _service() is not { } s) return;
        var name = _prompt("Rename design", "New name", row.Name);
        if (name is null) return;
        if (DeskDesigns.NormalizeName(name) is not { } clean || clean == row.Name) return;
        Done(s.Rename(row.Id, clean));
    }

    [RelayCommand]
    private void Pin(DesignRow? row)
    {
        if (row is null || _service() is not { } s) return;
        Done(s.Pin(row.Id, !row.Pinned));
    }

    [RelayCommand]
    private void Delete(DesignRow? row)
    {
        if (row is null || _service() is not { } s) return;
        Done(s.Delete(row.Id));
    }

    [RelayCommand(CanExecute = nameof(CanPutBack))]
    private void PutBack()
    {
        if (_service() is not { } s) return;
        Done(s.PutBack());
    }

    private static string? DefaultPrompt(string title, string prompt, string initial)
    {
        var window = new TextPromptWindow(title, prompt, initial);
        if (Application.Current?.Windows.OfType<SettingsWindow>().FirstOrDefault() is { IsLoaded: true } owner) window.Owner = owner;
        return window.ShowDialog() == true ? window.Value : null;
    }
}
