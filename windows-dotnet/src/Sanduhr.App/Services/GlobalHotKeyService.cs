using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Interop;
using Sanduhr.App.Interop;
using Sanduhr.Core;

namespace Sanduhr.App.Services;

/// <summary>
/// Owns the global shortcuts: one message-only window receives <c>WM_HOTKEY</c>, each enabled
/// action is registered with its combo, and a registration the OS refuses is kept as the
/// action's last failure so the Settings row can say so. Everything runs on the UI thread.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class GlobalHotKeyService : IDisposable
{
    private const int IdBase = 0x5A00;

    private readonly Action<HotKeyAction> _onHotKey;
    private readonly HwndSource _source;
    private readonly Dictionary<HotKeyAction, HotKeySetting> _desired = new();
    private readonly Dictionary<HotKeyAction, HotKeyCombo> _registered = new();
    private readonly Dictionary<HotKeyAction, HotKeyRefusal> _failures = new();
    private bool _suspended;
    private bool _disposed;

    /// <summary>Raised when an action's last failure changes (set, cleared, or different).</summary>
    public event Action<HotKeyAction>? FailureChanged;

    public GlobalHotKeyService(Action<HotKeyAction> onHotKey)
    {
        _onHotKey = onHotKey;
        var parameters = new HwndSourceParameters("SanduhrHotKeys")
        {
            ParentWindow = HotKeyNative.HWND_MESSAGE,
            Width = 0,
            Height = 0,
            WindowStyle = 0,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    /// <summary>Why the action's combo was not registered, or null.</summary>
    public HotKeyRefusal? LastFailure(HotKeyAction action) =>
        _failures.TryGetValue(action, out var f) ? f : null;

    /// <summary>The settings now in force; re-registers only the actions whose combo or switch changed.</summary>
    public void Apply(IReadOnlyDictionary<HotKeyAction, HotKeySetting> settings)
    {
        if (_disposed) return;
        foreach (var (action, setting) in settings)
            _desired[action] = setting;
        Reconcile();
    }

    /// <summary>Release every combo (the recorder needs them free to hear them).</summary>
    public void Suspend()
    {
        if (_disposed || _suspended) return;
        _suspended = true;
        foreach (var action in _registered.Keys.ToArray())
            Unregister(action);
    }

    /// <summary>Register what the settings ask for again.</summary>
    public void Resume()
    {
        if (_disposed || !_suspended) return;
        _suspended = false;
        Reconcile();
    }

    private void Reconcile()
    {
        if (_suspended) return;
        foreach (var (action, setting) in _desired)
        {
            HotKeyCombo? want = setting.On ? setting.Combo : null;
            if (_registered.TryGetValue(action, out var held))
            {
                if (want == held) continue;
                Unregister(action);
            }
            if (want is null)
            {
                SetFailure(action, null);
                continue;
            }
            Register(action, want.Value);
        }
    }

    private void Register(HotKeyAction action, HotKeyCombo combo)
    {
        var ok = HotKeyNative.RegisterHotKey(_source.Handle, IdOf(action),
            (uint)combo.Modifiers | HotKeyNative.MOD_NOREPEAT, (uint)combo.VirtualKey);
        if (ok)
        {
            _registered[action] = combo;
            SetFailure(action, null);
            return;
        }
        var error = Marshal.GetLastWin32Error();
        SetFailure(action, error == HotKeyNative.ERROR_HOTKEY_ALREADY_REGISTERED
            ? HotKeys.OsTakenNote(combo)
            : new HotKeyRefusal("os", $"Windows would not register {combo.Display} (error {error}). Pick another."));
    }

    private void Unregister(HotKeyAction action)
    {
        if (_registered.Remove(action))
            HotKeyNative.UnregisterHotKey(_source.Handle, IdOf(action));
    }

    private void SetFailure(HotKeyAction action, HotKeyRefusal? failure)
    {
        _failures.TryGetValue(action, out var before);
        if (Equals(before, failure)) return;
        if (failure is null) _failures.Remove(action);
        else _failures[action] = failure;
        FailureChanged?.Invoke(action);
    }

    private static int IdOf(HotKeyAction action) => IdBase + (int)action;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == HotKeyNative.WM_HOTKEY)
        {
            var id = wParam.ToInt32() - IdBase;
            if (Enum.IsDefined(typeof(HotKeyAction), id))
            {
                handled = true;
                _onHotKey((HotKeyAction)id);
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        foreach (var action in _registered.Keys.ToArray())
            Unregister(action);
        _disposed = true;
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
