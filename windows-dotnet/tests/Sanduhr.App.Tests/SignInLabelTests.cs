using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings v2, slice 5, the Done line's third clause: "the first sign-in asks for a label" (WF30). The embedded sign-in's save,
/// over an in-memory registry and scratch folders: nothing reaches Credential Manager or a browser.
/// </summary>
[Collection("SettingsWindow")]
public sealed class SignInLabelTests : AppWindowFixture
{
    private (SignInCoordinator Coordinator, AccountStore Accounts, List<string> Offered) Coordinator(Func<string, string?> answer,
        params string[] existing)
    {
        var accounts = new AccountStore(new MemoryCredentials());
        foreach (var label in existing)
            accounts.AddAccount(label, "placeholder-" + label);
        var offered = new List<string>();
        var coordinator = new SignInCoordinator(accounts, ScratchPaths(), suggestion =>
        {
            offered.Add(suggestion);
            return answer(suggestion);
        });
        return (coordinator, accounts, offered);
    }

    private static CapturedCookies Cookies() => new("sk-ant-placeholder", null);

    [Fact]
    public void The_first_sign_in_asks_for_a_label_with_Personal_filled_in()
    {
        var (coordinator, accounts, offered) = Coordinator(_ => "Work");

        var saved = coordinator.PersistEmbedded(Cookies());

        Assert.Equal([AccountLabels.First], offered);
        Assert.Equal("Work", saved);
        Assert.Equal(["Work"], accounts.ListAccounts());
        Assert.Equal("Work", accounts.GetActive());
        Assert.Equal(AccountOrigin.Embedded, accounts.GetOrigin("Work"));
    }

    [Fact]
    public void Cancelling_the_first_label_keeps_Personal_and_the_sign_in()
    {
        var (coordinator, accounts, _) = Coordinator(_ => null);

        Assert.Equal("Personal", coordinator.PersistEmbedded(Cookies()));
        Assert.Equal(["Personal"], accounts.ListAccounts());
        Assert.Equal("sk-ant-placeholder", accounts.LoadCredentials("Personal").SessionKey);
    }

    [Fact]
    public void A_later_sign_in_offers_the_next_free_number_and_refuses_a_taken_label()
    {
        var (coordinator, accounts, offered) = Coordinator(_ => "personal", "Personal");

        Assert.Equal("Account 2", coordinator.PersistEmbedded(Cookies()));
        Assert.Equal(["Account 2"], offered);
        Assert.Equal("Account 2", accounts.GetActive());
    }
}
