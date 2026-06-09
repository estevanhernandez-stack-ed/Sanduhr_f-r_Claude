using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sanduhr.Core;

/// <summary>An account's resolved secrets. Mirrors the Python dict
/// <c>{"session_key": ..., "cf_clearance": ...}</c>.</summary>
public sealed record Credentials(string? SessionKey, string? CfClearance);

/// <summary>
/// Multi-account registry layered on the OS credential manager, ported 1:1 from
/// <c>accounts.py</c>.
///
/// Per-account secrets live under named slots <c>sessionKey:{label}</c> /
/// <c>cf_clearance:{label}</c>; the registry itself under the fixed slots
/// <c>accounts:list</c> (JSON array of labels) and <c>accounts:active</c> (the
/// active label). Legacy single-slot installs (<c>sessionKey</c> /
/// <c>cf_clearance</c> without a label) auto-promote to a <c>Personal</c>
/// account on the first <see cref="MigrateLegacy"/> call.
///
/// The credential backend is injected (<see cref="ICredentialManager"/>) so the
/// same logic runs against the real Windows Credential Manager in production and
/// an in-memory fake under test — exactly the Python <c>_fake_keyring</c> split.
/// </summary>
public sealed class AccountStore
{
    /// <summary>Keyring service name — shared with <see cref="CredentialStore"/>.</summary>
    public const string Service = "com.626labs.sanduhr";

    private const string ListSlot = "accounts:list";
    private const string ActiveSlot = "accounts:active";

    // KEYRING SLOT NAMES (storage identifiers), not credential values. They must
    // match exactly what v2.0.x / v2.1.x wrote so MigrateLegacy() can read
    // pre-v2.2.0 credentials. Do not rename or treat as secrets.
    private const string LegacySessionSlot = "sessionKey";
    private const string LegacyCfSlot = "cf_clearance";

    private static readonly Regex LabelRe =
        new("^[A-Za-z0-9 _-]{1,32}$", RegexOptions.Compiled);

    private readonly ICredentialManager _cred;

    public AccountStore(ICredentialManager cred)
    {
        _cred = cred;
    }

    private static void ValidateLabel(string label)
    {
        if (!LabelRe.IsMatch(label))
            throw new ArgumentException(
                $"Invalid account label '{label}'. Must be 1-32 chars, " +
                "letters/digits/space/underscore/hyphen only.", nameof(label));
    }

    private List<string> ReadList()
    {
        var raw = _cred.GetPassword(ListSlot);
        if (string.IsNullOrEmpty(raw))
            return new List<string>();
        return JsonSerializer.Deserialize<List<string>>(raw) ?? new List<string>();
    }

    private void WriteList(List<string> labels)
        => _cred.SetPassword(ListSlot, JsonSerializer.Serialize(labels));

    private void DeleteSafely(string slot) => _cred.DeletePassword(slot);

    public IReadOnlyList<string> ListAccounts() => ReadList();

    public string? GetActive() => _cred.GetPassword(ActiveSlot);

    public void SetActive(string label)
    {
        if (!ReadList().Contains(label))
            throw new ArgumentException($"Account '{label}' not in registry", nameof(label));
        _cred.SetPassword(ActiveSlot, label);
    }

    public void AddAccount(string label, string sessionKey, string? cfClearance = null)
    {
        ValidateLabel(label);
        var labels = ReadList();
        if (labels.Contains(label))
            throw new ArgumentException($"Account '{label}' already exists", nameof(label));
        _cred.SetPassword($"sessionKey:{label}", sessionKey);
        if (!string.IsNullOrEmpty(cfClearance))
            _cred.SetPassword($"cf_clearance:{label}", cfClearance);
        labels.Add(label);
        WriteList(labels);
        if (GetActive() is null)
            SetActive(label);
    }

    public void RemoveAccount(string label)
    {
        var labels = ReadList();
        if (!labels.Contains(label))
            return;
        DeleteSafely($"sessionKey:{label}");
        DeleteSafely($"cf_clearance:{label}");
        labels.Remove(label);
        WriteList(labels);
        if (GetActive() == label)
        {
            if (labels.Count > 0)
                SetActive(labels[0]);
            else
                DeleteSafely(ActiveSlot);
        }
    }

    public void RenameAccount(string oldLabel, string newLabel)
    {
        ValidateLabel(newLabel);
        var labels = ReadList();
        if (!labels.Contains(oldLabel))
            throw new ArgumentException($"Account '{oldLabel}' not in registry", nameof(oldLabel));
        if (labels.Contains(newLabel))
            throw new ArgumentException($"Account '{newLabel}' already exists", nameof(newLabel));
        var creds = LoadCredentials(oldLabel);
        if (creds.SessionKey is not null)
            _cred.SetPassword($"sessionKey:{newLabel}", creds.SessionKey);
        if (creds.CfClearance is not null)
            _cred.SetPassword($"cf_clearance:{newLabel}", creds.CfClearance);
        DeleteSafely($"sessionKey:{oldLabel}");
        DeleteSafely($"cf_clearance:{oldLabel}");
        labels[labels.IndexOf(oldLabel)] = newLabel;
        WriteList(labels);
        if (GetActive() == oldLabel)
            SetActive(newLabel);
    }

    public Credentials LoadCredentials(string label) => new(
        _cred.GetPassword($"sessionKey:{label}"),
        _cred.GetPassword($"cf_clearance:{label}"));

    public void SaveCredentials(string label, string? sessionKey = null, string? cfClearance = null)
    {
        if (!ReadList().Contains(label))
            throw new ArgumentException($"Account '{label}' not in registry", nameof(label));
        if (sessionKey is not null)
            _cred.SetPassword($"sessionKey:{label}", sessionKey);
        if (cfClearance is not null)
            _cred.SetPassword($"cf_clearance:{label}", cfClearance);
    }

    /// <summary>
    /// Promote legacy single-slot creds to a named account. Returns true if
    /// migration ran, false on no-op (registry already populated, or no legacy
    /// creds found).
    /// </summary>
    public bool MigrateLegacy(string defaultName = "Personal")
    {
        if (ReadList().Count > 0)
            return false;
        var legacySession = _cred.GetPassword(LegacySessionSlot);
        if (string.IsNullOrEmpty(legacySession))
            return false;
        var legacyCf = _cred.GetPassword(LegacyCfSlot);
        AddAccount(defaultName, legacySession, legacyCf);
        DeleteSafely(LegacySessionSlot);
        DeleteSafely(LegacyCfSlot);
        return true;
    }
}
