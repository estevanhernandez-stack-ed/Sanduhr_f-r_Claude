namespace Sanduhr.Core;

/// <summary>
/// The 626 Labs agent API key, kept in the OS credential manager under its own
/// clearly named slot — never in settings.json, never logged. Rides the same
/// <see cref="ICredentialManager"/> seam as the claude.ai cookies, so the
/// on-disk entry is Windows Credential Manager target
/// <c>626labs:agentKey@com.626labs.sanduhr</c> (the <c>{slot}@{service}</c>
/// compound the keyring-compatible backend writes). Machine-scoped, not
/// per-account: one dashboard key per Windows user.
/// </summary>
public sealed class AgentKeyStore
{
    /// <summary>The credential slot. Storage identifier, not a secret.</summary>
    public const string Slot = "626labs:agentKey";

    private readonly ICredentialManager _cred;

    public AgentKeyStore(ICredentialManager cred) => _cred = cred;

    /// <summary>The stored key, or null when none is saved.</summary>
    public string? Load()
    {
        var v = _cred.GetPassword(Slot);
        return string.IsNullOrEmpty(v) ? null : v;
    }

    public bool HasKey => Load() is not null;

    /// <summary>Store (or overwrite) the key. Whitespace is trimmed; an empty
    /// value clears the slot instead of storing a blank credential.</summary>
    public void Save(string key)
    {
        var trimmed = key?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            Clear();
            return;
        }
        _cred.SetPassword(Slot, trimmed);
    }

    public bool Clear() => _cred.DeletePassword(Slot);
}
