using System.IO;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings ▸ History ▸ Clear history acts on the account the "Account:" picker shows.</summary>
[Collection("SettingsWindow")]
public sealed class HistoryClearTests : AppWindowFixture
{
    private sealed class MemoryCredentials : ICredentialManager
    {
        private readonly Dictionary<string, string> _store = new();
        public string Service => AccountStore.Service;
        public string? GetPassword(string slot) => _store.GetValueOrDefault(slot);
        public void SetPassword(string slot, string value) => _store[slot] = value;
        public bool DeletePassword(string slot) => _store.Remove(slot);
    }

    private Paths ScratchPaths() => new(Path.Combine(Root, "appdata"), Path.Combine(Root, "home"), Path.Combine(Root, "local"));

    /// <summary>A real (not demo) widget over scratch folders and an in-memory registry: Work active, Personal second.</summary>
    private (WidgetViewModel Widget, HistoryTabViewModel Tab, List<string> Confirmations) Open()
    {
        var accounts = new AccountStore(new MemoryCredentials());
        accounts.AddAccount("Work", "placeholder-work");
        accounts.AddAccount("Personal", "placeholder-personal");
        accounts.SetActive("Work");
        var widget = new WidgetViewModel(ScratchPaths(), accounts);
        widget.History.AppendForAccount("five_hour", 41, "Work");
        widget.History.AppendForAccount("five_hour", 17, "Personal");
        var confirmations = new List<string>();
        var tab = new HistoryTabViewModel(widget)
        {
            ConfirmHook = text => { confirmations.Add(text); return true; },
            InfoHook = _ => { },
        };
        return (widget, tab, confirmations);
    }

    private static void Pick(HistoryTabViewModel tab, string? account) =>
        tab.SelectedOption = tab.AccountOptions.Single(o => o.Account == account);

    [Fact]
    public void ClearingThePickedAccountEmptiesThatAccountAndLeavesTheActiveOneAlone() => AppHost.Run(() =>
    {
        var (widget, tab, _) = Open();
        Pick(tab, "Personal");

        tab.ClearHistoryCommand.Execute(null);

        Assert.Empty(widget.History.Load("five_hour", "Personal"));
        Assert.Equal([41], widget.History.Load("five_hour", "Work"));
    });

    [Fact]
    public void ClearingAllAccountsEmptiesEveryAccountButKeepsTheFiles() => AppHost.Run(() =>
    {
        var (widget, tab, _) = Open();
        Pick(tab, null);

        tab.ClearHistoryCommand.Execute(null);

        Assert.Empty(widget.History.Load("five_hour", "Work"));
        Assert.Empty(widget.History.Load("five_hour", "Personal"));
        var paths = ScratchPaths();
        Assert.True(File.Exists(paths.HistoryFileFor("Work")));
        Assert.True(File.Exists(paths.HistoryFileFor("Personal")));
    });

    [Fact]
    public void TheConfirmationNamesWhatItRemoves() => AppHost.Run(() =>
    {
        var (_, tab, confirmations) = Open();

        Pick(tab, "Personal");
        tab.ClearHistoryCommand.Execute(null);
        Pick(tab, null);
        tab.ClearHistoryCommand.Execute(null);

        Assert.Equal(2, confirmations.Count);
        Assert.Contains("30-day usage history of Personal", confirmations[0]);
        Assert.DoesNotContain("Work", confirmations[0]);
        Assert.Contains("30-day usage history of every account", confirmations[1]);
    });

    [Fact]
    public void DecliningTheConfirmationClearsNothing() => AppHost.Run(() =>
    {
        var (widget, tab, _) = Open();
        tab.ConfirmHook = _ => false;
        Pick(tab, "Personal");

        tab.ClearHistoryCommand.Execute(null);

        Assert.Equal([17], widget.History.Load("five_hour", "Personal"));
        Assert.Equal([41], widget.History.Load("five_hour", "Work"));
    });
}
