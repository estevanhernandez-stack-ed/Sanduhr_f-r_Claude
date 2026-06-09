using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sanduhr.Core;

/// <summary>Status of a v1 → v2 migration. Mirrors the Python dict returned by
/// <c>credentials.migrate_from_v1()</c>. <see cref="LegacyCleaned"/> == false
/// means the v1 plaintext credential file is still on disk — the caller should
/// surface that so the user can delete it.</summary>
public sealed record V1MigrationResult(
    bool Migrated,
    bool SessionKey,
    bool Theme,
    bool History,
    bool LegacyCleaned);

/// <summary>
/// Credential storage — the stable public API (<see cref="Load"/> /
/// <see cref="Save"/> / <see cref="Clear"/> / <see cref="MigrateFromV1"/>) that
/// the widget + settings dialog call, ported 1:1 from <c>credentials.py</c>.
/// Delegates to <see cref="AccountStore"/>, operating on the ACTIVE account by
/// default. On first save with no active account it auto-creates a
/// <c>Personal</c> account (the first-time sign-in flow).
/// </summary>
public sealed class CredentialStore
{
    /// <summary>Preserved constant — same service name as <see cref="AccountStore"/>.</summary>
    public const string Service = AccountStore.Service;

    private const string DefaultAccountName = "Personal";

    private readonly AccountStore _accounts;
    private readonly Paths _paths;

    public CredentialStore(AccountStore accounts, Paths paths)
    {
        _accounts = accounts;
        _paths = paths;
    }

    /// <summary>
    /// Active account's credentials, or <c>{null, null}</c> when no active
    /// account is configured.
    /// </summary>
    public Credentials Load()
    {
        var active = _accounts.GetActive();
        return active is null ? new Credentials(null, null) : _accounts.LoadCredentials(active);
    }

    /// <summary>
    /// Save credentials into the active account. If none exists AND a
    /// <paramref name="sessionKey"/> is provided, auto-create a <c>Personal</c>
    /// account (preserves the pre-multi-account first-time sign-in UX). No-op if
    /// there's no active account AND no session key.
    /// </summary>
    public void Save(string? sessionKey = null, string? cfClearance = null)
    {
        var active = _accounts.GetActive();
        if (active is null)
        {
            if (string.IsNullOrEmpty(sessionKey))
                return;
            _accounts.AddAccount(DefaultAccountName, sessionKey, cfClearance);
            return;
        }
        _accounts.SaveCredentials(active, sessionKey, cfClearance);
    }

    /// <summary>
    /// Remove the active account from the registry (the sign-out flow). Advances
    /// the active pointer to the first remaining account if any. No-op if no
    /// active account. Does NOT wipe the per-account history file — callers
    /// handle that explicitly.
    /// </summary>
    public void Clear()
    {
        var active = _accounts.GetActive();
        if (active is null)
            return;
        _accounts.RemoveAccount(active);
    }

    /// <summary>
    /// Migrate v1's plaintext config + history into v2 storage, then promote any
    /// pre-v2.2.0 single-slot keyring credentials to a <c>Personal</c> account.
    /// </summary>
    public V1MigrationResult MigrateFromV1()
    {
        var legacy = _paths.LegacyConfigFile;
        if (!File.Exists(legacy))
        {
            // No v1 config. Still attempt the v2.0.x → v2.2.0 promotion.
            _accounts.MigrateLegacy();
            return new V1MigrationResult(false, false, false, false, false);
        }

        JsonObject? cfg;
        try
        {
            cfg = JsonNode.Parse(File.ReadAllText(legacy)) as JsonObject;
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            _accounts.MigrateLegacy();
            return new V1MigrationResult(false, false, false, false, false);
        }

        bool sessionKeyMigrated = false, themeMigrated = false, historyMigrated = false, legacyCleaned = false;

        var sk = AsString(cfg?["session_key"]);
        if (!string.IsNullOrEmpty(sk))
        {
            Save(sessionKey: sk);
            sessionKeyMigrated = true;
        }

        var theme = AsString(cfg?["theme"]);
        if (!string.IsNullOrEmpty(theme))
        {
            var settingsPath = _paths.SettingsFile;
            JsonObject settings = new();
            if (File.Exists(settingsPath))
            {
                try
                {
                    settings = JsonNode.Parse(File.ReadAllText(settingsPath)) as JsonObject ?? new JsonObject();
                }
                catch (JsonException)
                {
                    settings = new JsonObject();
                }
            }
            settings["theme"] = theme;
            File.WriteAllText(settingsPath, settings.ToJsonString());
            themeMigrated = true;
        }

        var legacyHist = _paths.LegacyHistoryFile;
        if (File.Exists(legacyHist))
        {
            try
            {
                File.WriteAllText(_paths.HistoryFile, File.ReadAllText(legacyHist));
                historyMigrated = true;
            }
            catch (IOException)
            {
                // best-effort — leave historyMigrated false
            }
        }

        try
        {
            File.Delete(legacy);
            legacyCleaned = true;
        }
        catch (IOException)
        {
            // plaintext v1 config remains on disk; caller is told via the flag
        }

        // In case any v2.0.x keyring slots somehow co-exist, promote them.
        _accounts.MigrateLegacy();

        return new V1MigrationResult(true, sessionKeyMigrated, themeMigrated, historyMigrated, legacyCleaned);
    }

    private static string? AsString(JsonNode? node)
    {
        if (node is null)
            return null;
        try
        {
            return node.GetValue<string>();
        }
        catch
        {
            return node.ToString();
        }
    }
}
