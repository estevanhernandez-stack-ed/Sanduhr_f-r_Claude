using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>Set up Sanduhr (setup spec, sections 2 and 4): three ticks, the lines a change says, Continue and Not now or Cancel.
/// Streamer mode: nothing here draws a name, path or usage; the lines are fixed copy and the host masks any path it returns.</summary>
internal sealed partial class SetupViewModel : ObservableObject
{
    private readonly SetupService _service;

    internal SetupViewModel(SetupService service, bool firstRun)
    {
        _service = service;
        FirstRun = firstRun;
        var now = firstRun ? SetupChoices.FirstRun : service.Current();
        _desktop = now.Desktop;
        _drawers = now.Drawers;
        _claudeUsage = now.ClaudeUsage;
        Refresh();
    }

    /// <summary>A first run (or no setup saved yet): the default ticks and Not now. Otherwise the ticks show what is on, with Cancel.</summary>
    public bool FirstRun { get; }
    public string DismissLabel => FirstRun ? SetupCopy.NotNow : SetupCopy.Cancel;

    /// <summary>What unticking does, said before Continue applies it (spec 4). Empty on a first run: nothing is taken away.</summary>
    public ObservableCollection<string> ChangeLines { get; } = [];

    /// <summary>What Continue said when it could not finish (the choices could not be saved): shown in the window, which stays open.</summary>
    public ObservableCollection<string> ResultLines { get; } = [];

    /// <summary>Continue saved the choices. Closing any other way saves nothing.</summary>
    public bool Saved { get; private set; }

    [ObservableProperty] private bool _desktop;
    [ObservableProperty] private bool _drawers;
    [ObservableProperty] private bool _claudeUsage;
    [ObservableProperty] private string? _blocked;

    public bool CanContinue => Blocked is null;
    public bool HasChangeLines => ChangeLines.Count > 0;
    public bool HasResultLines => ResultLines.Count > 0;

    partial void OnDesktopChanged(bool value) => Refresh();
    partial void OnDrawersChanged(bool value) => Refresh();
    partial void OnClaudeUsageChanged(bool value) => Refresh();
    partial void OnBlockedChanged(string? value)
    {
        OnPropertyChanged(nameof(CanContinue));
        ContinueCommand.NotifyCanExecuteChanged();
    }

    private SetupChoices Choices => new(Desktop, Drawers, ClaudeUsage);

    private void Refresh()
    {
        Blocked = SetupChange.Blocked(Choices);
        ChangeLines.Clear();
        if (!FirstRun)
            foreach (var line in _service.Preview(Choices)) ChangeLines.Add(line);
        ResultLines.Clear();
        OnPropertyChanged(nameof(HasChangeLines));
        OnPropertyChanged(nameof(HasResultLines));
    }

    /// <summary>Continue applied the choices; the result's lines are notes for the caller to show.</summary>
    public event Action<SetupResult>? Applied;

    /// <summary>Not now or Cancel.</summary>
    public event Action? Dismissed;

    /// <summary>Opens Design it with an AI chat (set by App).</summary>
    public Action? DesignWithChatRequested { get; set; }

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private void Continue()
    {
        var result = _service.Apply(Choices);   // on the UI thread: a turn-off asks a question first
        if (result.Lines.Contains(SetupCopy.SaveFailed))
        {
            // Nothing was saved: stay open with the line, so the person can try again or back out.
            ResultLines.Clear();
            foreach (var line in result.Lines) ResultLines.Add(line);
            OnPropertyChanged(nameof(HasResultLines));
            return;
        }
        Saved = true;
        Applied?.Invoke(result);
    }

    [RelayCommand]
    private void Dismiss() => Dismissed?.Invoke();

    [RelayCommand]
    private void DesignWithChat() => DesignWithChatRequested?.Invoke();
}
