using System.Diagnostics;
using Microsoft.Win32;

namespace Sanduhr.Core;

/// <summary>
/// Run-on-login (auto-start) management — ported from <c>startup.py</c>.
/// <b>Off by default everywhere</b>; the user opts in from Settings → General.
///
/// <para>Two mechanisms, by install type:</para>
/// <list type="bullet">
/// <item><b>Unpackaged</b> (Velopack <c>.exe</c> / GitHub build): a per-user
/// registry Run entry at
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Sanduhr</c> pointing at
/// the running executable. Fully toggleable at runtime.</item>
/// <item><b>Packaged</b> (MSIX / Store): declared as a <c>windows.startupTask</c>
/// manifest extension (<c>Enabled="false"</c>). Flipping it programmatically needs
/// the WinRT <c>StartupTask</c> API, which is not bundled, so the toggle deep-links
/// to Windows Settings → Startup apps instead. <b>Manifest note for item 11:</b>
/// the .NET tree has no <c>Package.appxmanifest</c> yet — when the MSIX packaging
/// project lands, add the <c>desktop:StartupTask</c> extension (TaskId
/// <c>SanduhrAutoStart</c>, <c>Enabled="false"</c>) so Sanduhr appears in that
/// Settings list.</item>
/// </list>
///
/// <para>The pure helpers (<see cref="IsPackaged"/>, <see cref="RunCommand"/>,
/// <see cref="ValueName"/>) plus the instance <see cref="StartupManager"/> — whose
/// run-key path, value name, packaged predicate, and settings-opener are all
/// injectable — keep this fully unit-testable without touching the real Run value
/// or launching anything.</para>
/// </summary>
public static class Startup
{
    /// <summary>The per-user Run key path under HKCU.</summary>
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>The Run value name. MUST match the installer's <c>[Registry]</c>
    /// ValueName so an install-time opt-in and a later in-app toggle stay
    /// consistent (guarded by a test, like the Python build).</summary>
    public const string ValueName = "Sanduhr";

    /// <summary>The deep-link URI for Windows Settings → Startup apps.</summary>
    public const string SettingsUri = "ms-settings:startupapps";

    /// <summary>The running executable path, or "" when unknown.</summary>
    public static string CurrentExecutable() => Environment.ProcessPath ?? "";

    /// <summary>True when the given executable looks MSIX-installed — heuristic:
    /// its path lives under <c>…\WindowsApps\</c> (ported from
    /// <c>is_packaged</c>). Pass <c>null</c> to test the current process.</summary>
    public static bool IsPackaged(string? executable = null)
    {
        var exe = executable ?? CurrentExecutable();
        return exe.Replace('/', '\\').ToLowerInvariant().Contains("windowsapps");
    }

    /// <summary>The Run-key value: the quoted path used to relaunch on login
    /// (ported from <c>run_command</c>). Pass <c>null</c> for the current process.</summary>
    public static string RunCommand(string? executable = null)
        => $"\"{executable ?? CurrentExecutable()}\"";
}

/// <summary>Result of <see cref="StartupManager.SetEnabled"/>: whether the
/// on-login state was actually written, and whether we punted to Windows
/// Settings (the packaged path). Ports <c>StartupOutcome</c>.</summary>
public readonly record struct StartupOutcome(bool Applied, bool OpenedSettings);

/// <summary>
/// Instance facade over the auto-start mechanisms. Production code uses
/// <see cref="CreateDefault"/> (real Run key, real packaged detection, real
/// Settings deep-link); tests inject a throwaway run-key path, a fixed packaged
/// predicate, and a no-op settings opener so no test touches the real
/// <c>…\Run\Sanduhr</c> value or launches Windows Settings.
/// </summary>
public sealed class StartupManager
{
    private readonly string _runKeyPath;
    private readonly string _valueName;
    private readonly Func<bool> _isPackaged;
    private readonly Action _openSettings;
    private readonly string? _executable;

    public StartupManager(
        string? runKeyPath = null,
        string? valueName = null,
        Func<bool>? isPackaged = null,
        Action? openSettings = null,
        string? executable = null)
    {
        _runKeyPath = runKeyPath ?? Startup.RunKeyPath;
        _valueName = valueName ?? Startup.ValueName;
        _isPackaged = isPackaged ?? (() => Startup.IsPackaged());
        _openSettings = openSettings ?? DefaultOpenSettings;
        _executable = executable;
    }

    /// <summary>The production manager: real Run key + value name, packaged
    /// detection off the current process, and a real Settings deep-link.</summary>
    public static StartupManager CreateDefault() => new();

    /// <summary>True when this install is MSIX-packaged (can't toggle the Run key).</summary>
    public bool IsPackaged() => _isPackaged();

    // The Windows registry is Windows-only by definition; Sanduhr ships only on
    // Windows (the App is net10.0-windows) and Core already does Windows-only work
    // (advapi32 P/Invoke in WindowsCredentialManager). Scope the platform analyzer
    // off the registry calls rather than annotating the public API — annotating
    // would push CA1416 into Sanduhr.Tests (net10.0, no platform), where the
    // throwaway-key round-trip runs on Windows anyway.
#pragma warning disable CA1416

    /// <summary>True if the HKCU Run entry exists (unpackaged read).</summary>
    public bool IsEnabledUnpackaged()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_runKeyPath);
        return key?.GetValue(_valueName) is not null;
    }

    /// <summary>Write (enabled) or remove (disabled) the HKCU Run entry.
    /// Idempotent — deleting an absent value is a no-op.</summary>
    public void SetEnabledUnpackaged(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(_runKeyPath);
        if (key is null)
            return;
        if (enabled)
            key.SetValue(_valueName, Startup.RunCommand(_executable), RegistryValueKind.String);
        else
            key.DeleteValue(_valueName, throwOnMissingValue: false);
    }
#pragma warning restore CA1416

    /// <summary>Best-effort current state. Packaged installs report the manifest
    /// default (<c>false</c>) since the StartupTask state isn't readable without
    /// WinRT; unpackaged reads the Run key.</summary>
    public bool IsEnabled() => IsPackaged() ? false : IsEnabledUnpackaged();

    /// <summary>Open Windows Settings → Startup apps (the packaged fallback).</summary>
    public void OpenStartupSettings() => _openSettings();

    /// <summary>Apply the on-login preference. Unpackaged writes/removes the Run
    /// key; packaged opens Windows Settings (can't toggle without WinRT).</summary>
    public StartupOutcome SetEnabled(bool enabled)
    {
        if (IsPackaged())
        {
            OpenStartupSettings();
            return new StartupOutcome(Applied: false, OpenedSettings: true);
        }
        SetEnabledUnpackaged(enabled);
        return new StartupOutcome(Applied: true, OpenedSettings: false);
    }

    private static void DefaultOpenSettings()
        => Process.Start(new ProcessStartInfo(Startup.SettingsUri) { UseShellExecute = true });
}
