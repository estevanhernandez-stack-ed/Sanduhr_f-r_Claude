namespace Sanduhr.Core;

/// <summary>
/// The publish token — whatever credential the user's chosen endpoint wants —
/// kept in the OS credential manager under its own clearly named slot, never
/// in settings.json, never logged. Rides the same <see cref="ICredentialManager"/>
/// seam as the claude.ai cookies, so the on-disk entry is Windows Credential
/// Manager target <c>publish:token@com.626labs.sanduhr</c> (the
/// <c>{slot}@{service}</c> compound the keyring-compatible backend writes).
/// Machine-scoped, not per-account: one token per Windows user.
///
/// The pre-3.5 build stored the same secret as <c>626labs:agentKey</c> (the
/// feature then had one destination). The first read after upgrade moves it:
/// read old, write new, delete old — so an existing token keeps working and
/// the legacy slot does not linger.
/// </summary>
public sealed class PublishTokenStore
{
    /// <summary>The credential slot. Storage identifier, not a secret.</summary>
    public const string Slot = "publish:token";

    /// <summary>The pre-3.5 slot, migrated on first read.</summary>
    public const string LegacySlot = "626labs:agentKey";

    private readonly ICredentialManager _cred;

    public PublishTokenStore(ICredentialManager cred) => _cred = cred;

    /// <summary>The stored token, or null when none is saved. Migrates a
    /// legacy-slot token into the new slot on the way through.</summary>
    public string? Load()
    {
        var v = _cred.GetPassword(Slot);
        if (!string.IsNullOrEmpty(v))
            return v;

        var legacy = _cred.GetPassword(LegacySlot);
        if (string.IsNullOrEmpty(legacy))
            return null;
        _cred.SetPassword(Slot, legacy);
        _cred.DeletePassword(LegacySlot);
        return legacy;
    }

    public bool HasToken => Load() is not null;

    /// <summary>Store (or overwrite) the token. Whitespace is trimmed; an empty
    /// value clears the slot instead of storing a blank credential.</summary>
    public void Save(string token)
    {
        var trimmed = token?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            Clear();
            return;
        }
        _cred.SetPassword(Slot, trimmed);
        _cred.DeletePassword(LegacySlot);   // a fresh save supersedes any leftover
    }

    /// <summary>Remove the token from both the current and the legacy slot.
    /// True when anything was there to remove.</summary>
    public bool Clear()
    {
        bool current = _cred.DeletePassword(Slot);
        bool legacy = _cred.DeletePassword(LegacySlot);
        return current || legacy;
    }
}
