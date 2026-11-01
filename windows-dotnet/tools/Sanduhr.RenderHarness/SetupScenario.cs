using System.IO;
using System.Reflection;
using System.Windows;
using Sanduhr.App.Desk;
using Sanduhr.App.Services;
using Sanduhr.App.ViewModels;
using Sanduhr.App.Views;
using Sanduhr.Core;

// Set up Sanduhr (setup spec, sections 2 to 4): the setup window on a first run and reopened later with usage unticked (so a change
// line shows), and the Design it with an AI chat window with a short paste that shows one finding, in obsidian and aurora. The
// service runs over a scratch SettingsStore in a temp folder and a do-nothing host, and the chat window's callbacks do nothing, so
// nothing touches drawers, the Desk, the clipboard or the network. Off screen, no activation. Not part of all.
//
//   --scenario setup
//
// PNGs: <out>/setup-<theme>-first-run.png, setup-<theme>-later.png, setup-<theme>-chat.png.
public static partial class Program
{
    sealed class SetupHarnessHost : ISetupHost
    {
        public bool DrawersOn { get; set; }
        public bool DrawersEverSorted => true;
        public int DrawerFiles => 0;
        public bool AddOnsInstalled => false;
        public bool UsageOn { get; set; }
        public bool HasAccount => true;
        public SetupDrawersOff TurnOffDrawers() => new(true, null);
        public void TurnOnDrawers() { }
        public string? SortNow() => null;
        public void SetUsage(bool on) { }
        public void ReloadDesk() { }
    }

    static async Task RenderSetup(dynamic vm, Assembly asm)
    {
        string root = Path.Combine(Path.GetTempPath(), "sanduhr-harness-setup-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var theme in new[] { "obsidian", "aurora" })
            {
                vm.ApplyThemeByKey(theme);
                vm.ApplyThemeResources();
                await Settle();
                var store = new SettingsStore(new Paths(Path.Combine(root, theme, "appdata"), Path.Combine(root, theme, "home"), Path.Combine(root, theme, "local")));

                // First run: default ticks, nothing saved yet.
                var first = new SetupWindow(new SetupViewModel(new SetupService(store, new SetupHarnessHost()), firstRun: true));
                await ShowAndSave(first, $"setup-{theme}-first-run");

                // Later: usage, desktop and drawers were all on; usage unticked shows a change line.
                store.SaveSetup(new SetupChoices(true, true, true).ToSettings(null));
                store.SaveDesk(store.LoadDesk() with { Enabled = true });
                var host = new SetupHarnessHost { DrawersOn = true, UsageOn = true };
                var later = new SetupViewModel(new SetupService(store, host), firstRun: false) { ClaudeUsage = false };
                await ShowAndSave(new SetupWindow(later), $"setup-{theme}-later");

                // The chat window with one finding in a short paste.
                var chat = new ChatDesignViewModel(() => ["1920x1080, main"], null,
                    node => PastedSetup.RefuseUndrawn(DeskDesign.Validate(node, DeskDesignContext.Unknown)), (_, _) => null);
                chat.PasteText = "{\"desk\":{\"name\":\"Calm\",\"layout\":{\"clock\":{\"anchor\":\"zz\"}}}}";
                Log.Add($"setup {theme} chat: {chat.Findings.Count} finding(s), summary '{chat.Summary}'");
                await ShowAndSave(new ChatDesignWindow(chat), $"setup-{theme}-chat");
            }
        }
        finally
        {
            vm.ApplyThemeByKey("obsidian");
            vm.ApplyThemeResources();
            try { Directory.Delete(root, true); } catch { }
        }
    }

    static async Task ShowAndSave(Window w, string name)
    {
        OffScreen(w);
        w.Show();
        w.Left = -20000;
        w.Top = -20000;
        await Settle();
        Save((FrameworkElement)System.Windows.Media.VisualTreeHelper.GetChild(w, 0), name);
        w.Close();
        await Settle();
    }
}
