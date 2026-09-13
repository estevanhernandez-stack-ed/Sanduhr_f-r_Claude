using Sanduhr.Core;

namespace Sanduhr.Tests;

public class AgentKeyStoreTests
{
    [Fact]
    public void Slot_is_its_own_clearly_named_credential_target()
    {
        Assert.Equal("626labs:agentKey", AgentKeyStore.Slot);
        Assert.Equal("626labs:agentKey@com.626labs.sanduhr",
            WindowsCredentialManager.CompoundTarget(AccountStore.Service, AgentKeyStore.Slot));
    }

    [Fact]
    public void Save_then_load_round_trips_and_trims()
    {
        var store = new AgentKeyStore(new FakeCredentialManager());
        Assert.Null(store.Load());
        Assert.False(store.HasKey);

        store.Save("  sk-626-abc  ");

        Assert.Equal("sk-626-abc", store.Load());
        Assert.True(store.HasKey);
    }

    [Fact]
    public void Saving_blank_clears_instead_of_storing_an_empty_credential()
    {
        var fake = new FakeCredentialManager();
        var store = new AgentKeyStore(fake);
        store.Save("sk-626-abc");
        store.Save("   ");
        Assert.Null(store.Load());
        Assert.Null(fake.GetPassword(AgentKeyStore.Slot));
    }

    [Fact]
    public void Clear_removes_the_slot_and_reports_whether_anything_was_there()
    {
        var store = new AgentKeyStore(new FakeCredentialManager());
        Assert.False(store.Clear());
        store.Save("k");
        Assert.True(store.Clear());
        Assert.False(store.HasKey);
    }

    [Fact]
    public void Key_never_collides_with_account_slots()
    {
        var fake = new FakeCredentialManager();
        var accounts = new AccountStore(fake);
        accounts.AddAccount("Personal", "sess");
        new AgentKeyStore(fake).Save("k");
        Assert.Equal("sess", accounts.LoadCredentials("Personal").SessionKey);
        Assert.Equal("Personal", accounts.GetActive());
    }
}
