using System.Runtime.Versioning;
using Sanduhr.Core;

namespace Sanduhr.Tests;

[SupportedOSPlatform("windows")]
public class PublishTokenStoreTests
{
    [Fact]
    public void Slot_is_a_generic_clearly_named_credential_target()
    {
        Assert.Equal("publish:token", PublishTokenStore.Slot);
        Assert.Equal("publish:token@com.626labs.sanduhr",
            WindowsCredentialManager.CompoundTarget(AccountStore.Service, PublishTokenStore.Slot));
        Assert.Equal("626labs:agentKey", PublishTokenStore.LegacySlot);
    }

    [Fact]
    public void Save_then_load_round_trips_and_trims()
    {
        var store = new PublishTokenStore(new FakeCredentialManager());
        Assert.Null(store.Load());
        Assert.False(store.HasToken);

        store.Save("  tok-abc  ");

        Assert.Equal("tok-abc", store.Load());
        Assert.True(store.HasToken);
    }

    [Fact]
    public void Saving_blank_clears_instead_of_storing_an_empty_credential()
    {
        var fake = new FakeCredentialManager();
        var store = new PublishTokenStore(fake);
        store.Save("tok-abc");
        store.Save("   ");
        Assert.Null(store.Load());
        Assert.Null(fake.GetPassword(PublishTokenStore.Slot));
    }

    [Fact]
    public void Clear_removes_the_slot_and_reports_whether_anything_was_there()
    {
        var store = new PublishTokenStore(new FakeCredentialManager());
        Assert.False(store.Clear());
        store.Save("k");
        Assert.True(store.Clear());
        Assert.False(store.HasToken);
    }

    [Fact]
    public void Token_never_collides_with_account_slots()
    {
        var fake = new FakeCredentialManager();
        var accounts = new AccountStore(fake);
        accounts.AddAccount("Personal", "sess");
        new PublishTokenStore(fake).Save("k");
        Assert.Equal("sess", accounts.LoadCredentials("Personal").SessionKey);
        Assert.Equal("Personal", accounts.GetActive());
    }

    // -- legacy slot migration ----------------------------------------------------

    [Fact]
    public void First_read_moves_a_legacy_agent_key_into_the_new_slot_and_deletes_the_old_one()
    {
        var fake = new FakeCredentialManager();
        fake.SetPassword(PublishTokenStore.LegacySlot, "sk-626-old");
        var store = new PublishTokenStore(fake);

        Assert.Equal("sk-626-old", store.Load());

        Assert.Equal("sk-626-old", fake.GetPassword(PublishTokenStore.Slot));
        Assert.Null(fake.GetPassword(PublishTokenStore.LegacySlot));
        Assert.True(store.HasToken);
        Assert.Equal("sk-626-old", store.Load());   // second read: served from the new slot
    }

    [Fact]
    public void A_token_already_in_the_new_slot_wins_over_a_legacy_leftover()
    {
        var fake = new FakeCredentialManager();
        fake.SetPassword(PublishTokenStore.Slot, "new");
        fake.SetPassword(PublishTokenStore.LegacySlot, "old");

        Assert.Equal("new", new PublishTokenStore(fake).Load());
    }

    [Fact]
    public void Saving_a_new_token_supersedes_any_legacy_leftover()
    {
        var fake = new FakeCredentialManager();
        fake.SetPassword(PublishTokenStore.LegacySlot, "old");
        var store = new PublishTokenStore(fake);

        store.Save("fresh");

        Assert.Equal("fresh", store.Load());
        Assert.Null(fake.GetPassword(PublishTokenStore.LegacySlot));
    }

    [Fact]
    public void Clear_also_removes_a_legacy_leftover()
    {
        var fake = new FakeCredentialManager();
        fake.SetPassword(PublishTokenStore.LegacySlot, "old");
        var store = new PublishTokenStore(fake);

        Assert.True(store.Clear());

        Assert.Null(fake.GetPassword(PublishTokenStore.LegacySlot));
        Assert.Null(fake.GetPassword(PublishTokenStore.Slot));
        Assert.False(store.HasToken);
    }

    [Fact]
    public void No_token_anywhere_reads_null_and_writes_nothing()
    {
        var fake = new FakeCredentialManager();
        Assert.Null(new PublishTokenStore(fake).Load());
        Assert.Null(fake.GetPassword(PublishTokenStore.Slot));
        Assert.Null(fake.GetPassword(PublishTokenStore.LegacySlot));
    }
}
