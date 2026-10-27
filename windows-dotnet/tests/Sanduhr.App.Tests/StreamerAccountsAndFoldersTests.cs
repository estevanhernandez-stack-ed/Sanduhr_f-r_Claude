using System.IO;
using System.Text.Json;
using Sanduhr.App.Services;
using Sanduhr.App.Updates;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>Settings v2, slice 7: Accounts, the Claude Code cards and their report and dialogs, and Privacy's folder names (spec, Reach
/// inside Settings; the slice 6 carry; CPs 1, 6 to 12 and 25).</summary>
[Collection("SettingsWindow")]
public sealed class StreamerAccountsAndFoldersTests : AppWindowFixture
{
    private (SettingsViewModel Svm, WidgetViewModel Widget) Make()
    {
        var statusline = JsonSerializer.Serialize(new
        {
            statusLine = new { type = "command", command = $"powershell -File \"{Path.Combine(Profile, ".claude-qxwork", "line.ps1")}\"" },
            enabledPlugins = new Dictionary<string, bool> { ["qxplugin@qxmarket"] = true, ["sanduhr-meters@inline"] = true },
        });
        Home(".claude-qxwork", statusline);
        Home(".claude-qxhome");
        var (widget, _) = ScratchWidget("Qxalpha", "Qxbeta");
        widget.SetAccountFolder("Qxalpha", Path.Combine(Profile, ".claude-qxwork"));
        var svm = new SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask,
            checkForUpdatesNow: () => Task.FromResult(new UpdateProbeResult(UpdateProbeStatus.UpToDate)), isStoreBuild: false);
        return (svm, widget);
    }

    private static void NoSentinel(string where, string? text) =>
        Assert.False((text ?? "").Contains("qx", StringComparison.OrdinalIgnoreCase), $"{where}: \"{text}\"");

    [Fact]
    public void Accounts_rows_names_and_messages_use_codenames() => AppHost.Run(() =>
    {
        var (svm, widget) = Make();
        try
        {
            widget.Streamer.SetOn(true);
            var accounts = svm.Accounts;
            accounts.Reload();
            foreach (var a in accounts.Accounts)
            {
                NoSentinel("row", a.Shown);
                NoSentinel("row spoken", a.ToString());
                Assert.StartsWith("Qx", a.Label);   // the key stays real
            }
            accounts.SelectedAccount = accounts.Accounts.First(a => a.Label == "Qxalpha");
            NoSentinel("clear name", accounts.ClearHistoryName);
            NoSentinel("remove name", accounts.RemoveName);
            NoSentinel("remove caption", accounts.RemoveCaption);
            NoSentinel("linked folder", accounts.LinkedFolderText);

            string? info = null;
            accounts.ConfirmClearHook = _ => true;
            accounts.InfoHook = m => info = m;
            accounts.ClearHistoryCommand.Execute(null);
            NoSentinel("clear question", accounts.LastQuestion);
            NoSentinel("cleared notice", info);
            accounts.ConfirmRemoveHook = _ => false;
            accounts.SignOutCommand.Execute(null);
            NoSentinel("remove question", accounts.LastQuestion);
        }
        finally { widget.Dispose(); }
    });

    /// <summary>Decision 12: a codename is never saved as an account's name.</summary>
    [Fact]
    public void The_rename_prompt_opens_empty_while_the_name_is_hidden() => AppHost.Run(() =>
    {
        var (svm, widget) = Make();
        try
        {
            Assert.Equal("Qxalpha", AccountsViewModel.RenamePrefill("Qxalpha"));
            widget.Streamer.SetOn(true);
            Assert.Equal("", AccountsViewModel.RenamePrefill("Qxalpha"));
            widget.Streamer.SetShown(CodenameCategory.Account, "Qxalpha", true);
            Assert.Equal("Qxalpha", AccountsViewModel.RenamePrefill("Qxalpha"));
            NoSentinel("rename error", AccountsViewModel.RenameError("Qxbeta", new InvalidOperationException("Account 'Qxbeta' already exists")));
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Claude_code_cards_their_names_and_the_linked_account_use_codenames() => AppHost.Run(() =>
    {
        var (svm, widget) = Make();
        try
        {
            widget.Streamer.SetOn(true);
            var page = svm.ClaudeCodePage;
            page.Reload();
            foreach (var row in page.Folders)
            {
                foreach (var (what, text) in new[]
                {
                    ("title", row.Display), ("card", row.AutomationName), ("combo name", row.LinkedAccountName), ("forget", row.ForgetName),
                    ("mcp switch", row.McpReadableName), ("holds", row.HoldsName), ("meters", row.MetersSpoken), ("meters link", row.MetersLinkName),
                })
                    NoSentinel(what, text);
                Assert.True(row.CanShowRealName);
            }
            Assert.All(page.AccountOptions, o => NoSentinel("combo item", o.Shown + o.ToString()));
            var work = page.Folders.Single(f => f.Path.EndsWith(".claude-qxwork", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("Qxalpha", work.LinkedAccount);   // the value stays the real label

            work.HoldsOpen = true;
            NoSentinel("statusline command", work.Holds.StatuslineCommand);
            Assert.All(work.Holds.PluginLines, l => NoSentinel("plugin", l.Name + l.AutomationName));
            Assert.Contains(work.Holds.PluginLines, l => l.Name == "sanduhr-meters@inline");   // allowlisted by default
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void A_cards_quick_route_shows_its_real_name() => AppHost.Run(() =>
    {
        var (svm, widget) = Make();
        try
        {
            widget.Streamer.SetOn(true);
            var page = svm.ClaudeCodePage;
            page.Reload();
            var work = page.Folders.Single(f => f.Path.EndsWith(".claude-qxwork", StringComparison.OrdinalIgnoreCase));
            work.ShowRealNameCommand.Execute(null);
            page.Reload();
            Assert.Equal(@"~\.claude-qxwork", page.Folders.Single(f => f.Path == work.Path).Display);
            Assert.False(page.Folders.Single(f => f.Path == work.Path).CanShowRealName);
        }
        finally { widget.Dispose(); }
    });

    /// <summary>The slice 6 carry: Privacy's folder-reads row names folders, masked as the cards are.</summary>
    [Fact]
    public void Privacys_folder_names_are_codenames() => AppHost.Run(() =>
    {
        var (svm, widget) = Make();
        try
        {
            widget.Streamer.SetOn(true);
            svm.Privacy.Reload();
            Assert.All(svm.Privacy.Agents, a => NoSentinel(a.Name, a.State + a.ToString()));
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Status_lines_and_dialog_text_show_no_folder_or_user_path() => AppHost.Run(() =>
    {
        var (_, widget) = Make();
        try
        {
            widget.Streamer.SetOn(true);
            var work = Path.Combine(Profile, ".claude-qxwork");
            var text = StreamerMode.FoldersIn($@"Couldn't write {work}\settings.json; see {Profile}\AppData\x.log", [work]);
            NoSentinel("status", text);
            Assert.Contains(StreamerMode.Mask.Folder(work), text);
            Assert.Contains(@"%USERPROFILE%\…\x.log", text);
            NoSentinel("mcp consent details", McpConsentDialog.DetailsFor(Path.Combine(Profile, ".sanduhr", "bin", "sanduhr-mcp.cmd"),
                Path.Combine(work, ".claude.json"), [work]));
        }
        finally { widget.Dispose(); }
    });

    /// <summary>Review Focus 3: Shuffle with Settings open renames every open page at once, and the allowlist survives.</summary>
    [Fact]
    public void Shuffle_renames_every_open_page_and_keeps_the_allowlist() => AppHost.Run(() =>
    {
        var (svm, widget) = Make();
        try
        {
            widget.Streamer.SetOn(true);
            svm.ReloadNames();
            widget.Streamer.SetShown(CodenameCategory.Account, "Qxbeta", true);
            var account = svm.Accounts.Accounts.Single(a => a.Label == "Qxalpha").Shown;
            var card = svm.ClaudeCodePage.Folders.Single(f => f.Path.EndsWith(".claude-qxwork", StringComparison.OrdinalIgnoreCase)).Display;
            svm.Privacy.Streamer!.ConfirmShuffleHook = () => true;
            // A fresh salt can, rarely, draw the same pair for a name, so each name gets up to four draws to move.
            string Account() => svm.Accounts.Accounts.Single(a => a.Label == "Qxalpha").Shown;
            string Card() => svm.ClaudeCodePage.Folders.Single(f => f.Path.EndsWith(".claude-qxwork", StringComparison.OrdinalIgnoreCase)).Display;
            for (int draw = 0; draw < 4 && (Account() == account || Card() == card); draw++)
            {
                svm.Privacy.Streamer.ShuffleCommand.Execute(null);
                svm.ReloadNames();   // the window's queued dispatcher turn (attached owners only); the pages rebuild through this
            }
            Assert.NotEqual(account, Account());
            Assert.NotEqual(card, Card());
            Assert.Equal("Qxbeta", svm.Accounts.Accounts.Single(a => a.Label == "Qxbeta").Shown);
        }
        finally { widget.Dispose(); }
    });

    /// <summary>Display only: the rename saves what the person typed, never the codename the page shows (controller ruling, Task 7's find).</summary>
    [Fact]
    public void Renaming_while_streamer_mode_is_on_saves_the_typed_name_and_never_the_codename() => AppHost.Run(() =>
    {
        var (svm, widget) = Make();
        try
        {
            widget.Streamer.SetOn(true);
            var accounts = svm.Accounts;
            accounts.Reload();
            var codename = accounts.Accounts.Single(a => a.Label == "Qxalpha").Shown;
            string? prefill = null;
            accounts.RenameHook = offered => { prefill = offered; return "Qxrenamed"; };
            accounts.SelectedAccount = accounts.Accounts.Single(a => a.Label == "Qxalpha");
            accounts.RenameCommand.Execute(null);

            Assert.Equal("", prefill);   // the prompt never offers the codename as text to save
            var labels = accounts.Accounts.Select(a => a.Label).ToList();
            Assert.Contains("Qxrenamed", labels);
            Assert.DoesNotContain("Qxalpha", labels);
            Assert.DoesNotContain(codename, labels);
            Assert.DoesNotContain(widget.ShownAccounts, l => l == codename);
            NoSentinel("row after rename", accounts.Accounts.Single(a => a.Label == "Qxrenamed").Shown);
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Cancelling_the_rename_prompt_changes_nothing() => AppHost.Run(() =>
    {
        var (svm, widget) = Make();
        try
        {
            widget.Streamer.SetOn(true);
            svm.Accounts.RenameHook = _ => null;
            svm.Accounts.SelectedAccount = svm.Accounts.Accounts.Single(a => a.Label == "Qxalpha");
            svm.Accounts.RenameCommand.Execute(null);
            Assert.Equal(["Qxalpha", "Qxbeta"], svm.Accounts.Accounts.Select(a => a.Label).OrderBy(l => l));
        }
        finally { widget.Dispose(); }
    });

    /// <summary>P18: the Remove detail names a history file; while the name is hidden the parenthesis is dropped, since no such file exists.</summary>
    [Fact]
    public void The_remove_caption_drops_the_history_file_name_while_the_name_is_hidden() => AppHost.Run(() =>
    {
        var (svm, widget) = Make();
        try
        {
            widget.Streamer.SetOn(true);
            svm.Accounts.Reload();
            svm.Accounts.SelectedAccount = svm.Accounts.Accounts.Single(a => a.Label == "Qxalpha");
            Assert.DoesNotContain("history.", svm.Accounts.RemoveCaption);
            widget.Streamer.SetShown(CodenameCategory.Account, "Qxalpha", true);
            svm.Accounts.Reload();
            svm.Accounts.SelectedAccount = svm.Accounts.Accounts.Single(a => a.Label == "Qxalpha");
            Assert.Contains("history.Qxalpha.json", svm.Accounts.RemoveCaption);
        }
        finally { widget.Dispose(); }
    });

    // P1, assigned to this task: every error message the sign-in flow puts on screen goes through Paths while streamer mode is on.
    private string IoMessage() => new UnauthorizedAccessException(
        $@"Access to the path '{Profile}\AppData\Local\Sanduhr\webview2\x' is denied.").Message;

    private static void NoUser(string where, string text) =>
        Assert.False(text.Contains(Environment.UserName, StringComparison.OrdinalIgnoreCase), $"{where}: \"{text}\"");

    [Fact]
    public void Sign_in_profile_failure_text_hides_the_user_folder() => AppHost.Run(() =>
    {
        var (_, widget) = Make();
        try
        {
            var ex = new IOException(IoMessage());
            Assert.Contains(Environment.UserName, SignInCoordinator.ProfileFailureText(ex), StringComparison.OrdinalIgnoreCase);   // off: unchanged
            widget.Streamer.SetOn(true);
            NoUser("profile failure", SignInCoordinator.ProfileFailureText(ex));
            Assert.Contains("Couldn't prepare the sign-in browser profile", SignInCoordinator.ProfileFailureText(ex));
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void Sign_in_failed_question_hides_the_user_folder_from_both_window_failures() => AppHost.Run(() =>
    {
        var (_, widget) = Make();
        try
        {
            widget.Streamer.SetOn(true);
            var start = new SignInResult.Failed($"Couldn't start the sign-in browser: {IoMessage()}");   // SignInWindow :122
            var save = new SignInResult.Failed($"Sign-in succeeded but saving the account failed: {IoMessage()}");   // :445
            NoUser("start failure", SignInCoordinator.FailedText(start.Message));
            NoUser("save failure", SignInCoordinator.FailedText(save.Message));
            Assert.EndsWith("Paste a sessionKey by hand instead?", SignInCoordinator.FailedText(start.Message));
        }
        finally { widget.Dispose(); }
    });

    [Fact]
    public void The_sign_in_windows_own_error_line_hides_the_user_folder() => AppHost.Run(() =>
    {
        var (_, widget) = Make();
        try
        {
            widget.Streamer.SetOn(true);
            NoUser("load error", SignInWindow.LoadErrorText($"Couldn't restart the sign-in browser: {IoMessage()}"));   // :319
        }
        finally { widget.Dispose(); }
    });
}
