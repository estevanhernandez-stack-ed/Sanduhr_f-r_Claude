using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

/// <summary>
/// Parity tests for Core/AccountStore.cs — ported from the Python build's
/// test_accounts.py (plus the registry-state intents from test_accounts_dialog.py;
/// the Qt-widget signal assertions there are UI and don't apply to Core). The
/// Python <c>_fake_keyring</c> fixture maps to an injected
/// <see cref="FakeCredentialManager"/>; <c>ValueError</c> maps to
/// <see cref="ArgumentException"/>.
/// </summary>
public class AccountStoreTests
{
    private static AccountStore New() => new(new FakeCredentialManager());

    [Fact]
    public void Empty_registry_returns_no_accounts()
    {
        var a = New();
        Assert.Empty(a.ListAccounts());
        Assert.Null(a.GetActive());
    }

    [Fact]
    public void Add_account_persists_and_sets_active_when_first()
    {
        var a = New();
        a.AddAccount("Personal", "placeholder-1", cfClearance: null);
        Assert.Equal(new[] { "Personal" }, a.ListAccounts());
        Assert.Equal("Personal", a.GetActive());
    }

    [Fact]
    public void Add_second_account_does_not_change_active()
    {
        var a = New();
        a.AddAccount("Personal", "placeholder-1");
        a.AddAccount("Work", "placeholder-2");
        Assert.Equal(new[] { "Personal", "Work" }, a.ListAccounts());
        Assert.Equal("Personal", a.GetActive());
    }

    [Fact]
    public void Set_active_changes_pointer()
    {
        var a = New();
        a.AddAccount("Personal", "placeholder-1");
        a.AddAccount("Work", "placeholder-2");
        a.SetActive("Work");
        Assert.Equal("Work", a.GetActive());
    }

    [Fact]
    public void Set_active_unknown_account_raises()
    {
        var a = New();
        a.AddAccount("Personal", "placeholder-1");
        Assert.Throws<ArgumentException>(() => a.SetActive("Nonexistent"));
    }

    [Fact]
    public void Remove_account_clears_creds_and_advances_active()
    {
        var a = New();
        a.AddAccount("Personal", "placeholder-1");
        a.AddAccount("Work", "placeholder-2");
        a.RemoveAccount("Personal");
        Assert.Equal(new[] { "Work" }, a.ListAccounts());
        Assert.Equal("Work", a.GetActive());
        Assert.Equal(new Credentials(null, null), a.LoadCredentials("Personal"));
    }

    [Fact]
    public void Remove_last_account_leaves_no_active()
    {
        var a = New();
        a.AddAccount("Solo", "placeholder-1");
        a.RemoveAccount("Solo");
        Assert.Empty(a.ListAccounts());
        Assert.Null(a.GetActive());
    }

    [Fact]
    public void Remove_unknown_account_is_noop()
    {
        var a = New();
        a.AddAccount("Personal", "placeholder-1");
        a.RemoveAccount("Nonexistent");
        Assert.Equal(new[] { "Personal" }, a.ListAccounts());
        Assert.Equal("Personal", a.GetActive());
    }

    [Fact]
    public void Rename_account_updates_list_active_and_keys()
    {
        var a = New();
        a.AddAccount("Personal", "placeholder-1", "cf-placeholder-1");
        a.RenameAccount("Personal", "Home");
        Assert.Equal(new[] { "Home" }, a.ListAccounts());
        Assert.Equal("Home", a.GetActive());
        Assert.Equal(new Credentials("placeholder-1", "cf-placeholder-1"), a.LoadCredentials("Home"));
        Assert.Equal(new Credentials(null, null), a.LoadCredentials("Personal"));
    }

    [Fact]
    public void Rename_to_existing_label_rejected()
    {
        var a = New();
        a.AddAccount("Personal", "placeholder-1");
        a.AddAccount("Work", "placeholder-2");
        Assert.Throws<ArgumentException>(() => a.RenameAccount("Personal", "Work"));
    }

    [Fact]
    public void Invalid_label_rejected()
    {
        var a = New();
        Assert.Throws<ArgumentException>(() => a.AddAccount("", "x"));
        Assert.Throws<ArgumentException>(() => a.AddAccount(new string('a', 33), "x"));
        Assert.Throws<ArgumentException>(() => a.AddAccount("bad/name", "x"));
    }

    [Fact]
    public void Duplicate_label_rejected()
    {
        var a = New();
        a.AddAccount("Personal", "placeholder-1");
        Assert.Throws<ArgumentException>(() => a.AddAccount("Personal", "placeholder-2"));
    }

    [Fact]
    public void Load_credentials_for_unknown_account_returns_nones()
    {
        var a = New();
        Assert.Equal(new Credentials(null, null), a.LoadCredentials("DoesNotExist"));
    }

    [Fact]
    public void Save_credentials_updates_existing_account()
    {
        var a = New();
        a.AddAccount("Personal", "placeholder-1");
        a.SaveCredentials("Personal", "placeholder-2", "cf-placeholder-2");
        Assert.Equal(new Credentials("placeholder-2", "cf-placeholder-2"), a.LoadCredentials("Personal"));
    }

    [Fact]
    public void Save_credentials_for_unknown_account_raises()
    {
        var a = New();
        Assert.Throws<ArgumentException>(() => a.SaveCredentials("Nonexistent", "x"));
    }

    [Fact]
    public void Migrate_legacy_promotes_existing_creds_to_personal()
    {
        var cred = new FakeCredentialManager();
        // Seed the legacy single-slot entries (the bare slot names v2.0.x wrote).
        cred.SetPassword("sessionKey", "placeholder-legacy");
        cred.SetPassword("cf_clearance", "cf-placeholder-legacy");
        var a = new AccountStore(cred);

        Assert.True(a.MigrateLegacy());
        Assert.Equal(new[] { "Personal" }, a.ListAccounts());
        Assert.Equal("Personal", a.GetActive());
        Assert.Equal(new Credentials("placeholder-legacy", "cf-placeholder-legacy"), a.LoadCredentials("Personal"));
        // Legacy slots cleaned up.
        Assert.Null(cred.GetPassword("sessionKey"));
        Assert.Null(cred.GetPassword("cf_clearance"));
    }

    [Fact]
    public void Migrate_legacy_noop_when_already_migrated()
    {
        var a = New();
        a.AddAccount("Personal", "placeholder-1");
        Assert.False(a.MigrateLegacy());
    }

    [Fact]
    public void Migrate_legacy_noop_when_no_creds()
    {
        var a = New();
        Assert.False(a.MigrateLegacy());
    }

    // -- origin flag (WS-A: origin-aware reauth) ------------------------------

    [Fact]
    public void Origin_defaults_to_embedded_when_never_set()
    {
        var a = New();
        a.AddAccount("Personal", "placeholder-1");
        Assert.Equal(AccountOrigin.Embedded, a.GetOrigin("Personal"));
    }

    [Fact]
    public void Origin_round_trips()
    {
        var a = New();
        a.AddAccount("Personal", "placeholder-1");
        a.SetOrigin("Personal", AccountOrigin.Manual);
        Assert.Equal(AccountOrigin.Manual, a.GetOrigin("Personal"));
        a.SetOrigin("Personal", AccountOrigin.Embedded);
        Assert.Equal(AccountOrigin.Embedded, a.GetOrigin("Personal"));
    }

    [Fact]
    public void Set_origin_unknown_account_raises()
    {
        var a = New();
        Assert.Throws<ArgumentException>(() => a.SetOrigin("Nonexistent", AccountOrigin.Manual));
    }

    [Fact]
    public void Remove_account_clears_origin()
    {
        var a = New();
        a.AddAccount("Personal", "placeholder-1");
        a.SetOrigin("Personal", AccountOrigin.Manual);
        a.RemoveAccount("Personal");
        // Re-adding the same label must not inherit the old origin.
        a.AddAccount("Personal", "placeholder-2");
        Assert.Equal(AccountOrigin.Embedded, a.GetOrigin("Personal"));
    }

    [Fact]
    public void Rename_carries_origin()
    {
        var a = New();
        a.AddAccount("Personal", "placeholder-1");
        a.SetOrigin("Personal", AccountOrigin.Manual);
        a.RenameAccount("Personal", "Home");
        Assert.Equal(AccountOrigin.Manual, a.GetOrigin("Home"));
    }
}
