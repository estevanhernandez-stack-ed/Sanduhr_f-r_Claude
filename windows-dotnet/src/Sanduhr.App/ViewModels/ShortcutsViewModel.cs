using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// Settings, General, Shortcuts: the global "open Settings" shortcut. Persists through
/// <see cref="SettingsStore"/>, applies through <see cref="GlobalHotKeyService"/>, and runs the
/// recorder: while recording the service is suspended so the combo being typed reaches the
/// window instead of firing. The window feeds keys to <see cref="TryRecord"/>.
/// </summary>
public sealed partial class ShortcutsViewModel : ObservableObject
{
    private const HotKeyAction Action = HotKeyAction.Settings;

    private readonly SettingsStore _store;
    private readonly GlobalHotKeyService? _service;
    private HotKeySetting _setting;
    private string _recordNote = "";

    [ObservableProperty] private bool _isRecording;

    public ShortcutsViewModel(SettingsStore store, GlobalHotKeyService? service)
    {
        _store = store;
        _service = service;
        _setting = store.LoadHotKeySetting(Action);
        if (_service is not null)
            _service.FailureChanged += OnFailureChanged;
    }

    /// <summary>"Ctrl+Alt+S opens Settings".</summary>
    public string Label => HotKeys.Label(Action, _setting.Combo);

    /// <summary>The combo, or "Type keys…" while recording.</summary>
    public string KeysText => IsRecording ? "Type keys…" : _setting.Combo.Display;

    public string KeysToolTip => IsRecording ? "Press the new keys, or Escape to cancel." : "Click, then press new keys.";

    public string KeysAutomationName =>
        IsRecording
            ? $"Recording keys, {HotKeys.Name(Action)}. Press the new keys, or Escape to cancel."
            : $"Change keys, {HotKeys.Name(Action)}";

    public string SwitchAutomationName => HotKeys.Name(Action);

    public string ResetText => $"Reset to {HotKeys.Default(Action).Display}";

    public string ResetAutomationName => $"Reset {HotKeys.Name(Action)} to {HotKeys.Default(Action).Display}";

    public bool IsOn
    {
        get => _setting.On;
        set
        {
            if (_setting.On == value) return;
            Commit(_setting with { On = value });
        }
    }

    public bool ShowReset => _setting.Combo != HotKeys.Default(Action);

    /// <summary>A refusal from the recorder, else why the OS would not register the combo; empty otherwise.</summary>
    public string Note =>
        _recordNote.Length > 0 ? _recordNote : _service?.LastFailure(Action)?.Note ?? "";

    public bool HasNote => Note.Length > 0;

    public string Caption => HotKeys.Caption(CurrentMap());

    partial void OnIsRecordingChanged(bool value)
    {
        OnPropertyChanged(nameof(KeysText));
        OnPropertyChanged(nameof(KeysToolTip));
        OnPropertyChanged(nameof(KeysAutomationName));
    }

    [RelayCommand]
    private void StartRecording()
    {
        if (IsRecording) return;
        _recordNote = "";
        _service?.Suspend();
        IsRecording = true;
        NotifyNote();
    }

    [RelayCommand]
    public void CancelRecording()
    {
        if (!IsRecording) return;
        IsRecording = false;
        _recordNote = "";
        _service?.Resume();
        NotifyNote();
    }

    [RelayCommand]
    private void Reset()
    {
        var wasRecording = IsRecording;
        IsRecording = false;
        _recordNote = "";
        Commit(_setting with { Combo = HotKeys.Default(Action) });
        if (wasRecording) _service?.Resume();
    }

    /// <summary>
    /// A key pressed while recording. Pure modifier keys are ignored; a refused combo sets the
    /// note and keeps recording; otherwise the combo is saved, recording ends and the service
    /// registers it (an OS refusal then lands in <see cref="Note"/>). Returns true when saved.
    /// </summary>
    public bool TryRecord(int vk, HotKeyModifiers mods)
    {
        if (!IsRecording || HotKeyCombo.IsModifierKey(vk)) return false;
        var combo = new HotKeyCombo(vk, mods);
        var refusal = HotKeys.Check(combo, Action, CurrentMap());
        if (refusal is not null)
        {
            _recordNote = refusal.Note;
            NotifyNote();
            return false;
        }

        _recordNote = "";
        IsRecording = false;
        Commit(_setting with { Combo = combo });
        _service?.Resume();
        return true;
    }

    /// <summary>The window is closing: stop recording and drop the service subscription.</summary>
    public void Detach()
    {
        CancelRecording();
        if (_service is not null)
            _service.FailureChanged -= OnFailureChanged;
    }

    private void Commit(HotKeySetting next)
    {
        _setting = next;
        _store.SaveHotKeySetting(Action, next);
        _service?.Apply(new Dictionary<HotKeyAction, HotKeySetting> { [Action] = next });
        OnPropertyChanged(nameof(IsOn));
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(KeysText));
        OnPropertyChanged(nameof(ShowReset));
        OnPropertyChanged(nameof(Caption));
        NotifyNote();
    }

    private IReadOnlyDictionary<HotKeyAction, HotKeyCombo> CurrentMap() =>
        new Dictionary<HotKeyAction, HotKeyCombo> { [Action] = _setting.Combo };

    private void OnFailureChanged(HotKeyAction action)
    {
        if (action == Action) NotifyNote();
    }

    private void NotifyNote()
    {
        OnPropertyChanged(nameof(Note));
        OnPropertyChanged(nameof(HasNote));
    }
}
