using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>
/// In-memory <see cref="ICredentialManager"/> — the C# equivalent of the Python
/// <c>_fake_keyring</c> fixture. Keyed by slot (the service is bound to the
/// instance, exactly as the real WinVault backend scopes by service). Used by
/// every account/credential/history test so no test touches the live
/// <c>com.626labs.sanduhr</c> Credential Manager entries.
/// </summary>
internal sealed class FakeCredentialManager : ICredentialManager
{
    private readonly Dictionary<string, string> _store = new();

    public string Service { get; }

    public FakeCredentialManager(string service = AccountStore.Service) => Service = service;

    public string? GetPassword(string slot) => _store.TryGetValue(slot, out var v) ? v : null;

    public void SetPassword(string slot, string value) => _store[slot] = value;

    public bool DeletePassword(string slot) => _store.Remove(slot);
}

/// <summary>
/// A throwaway temp directory that deletes itself on dispose — the C# stand-in
/// for the Python <c>tmp_appdata</c> / <c>monkeypatch.setenv("APPDATA", tmp)</c>
/// fixture. Tests inject its path into <see cref="Paths"/> instead of mutating
/// the process-global <c>%APPDATA%</c> (safe under parallel xUnit).
/// </summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "sanduhr-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch
        {
            // best-effort cleanup; a leaked temp dir never fails a test
        }
    }
}
