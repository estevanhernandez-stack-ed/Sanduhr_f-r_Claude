using System.Text;
using System.Text.Json;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public sealed class ReceiptLogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanduhr-rl-" + Guid.NewGuid().ToString("N"));
    private string Path_ => Path.Combine(_root, "integrations", "installs.json");

    public ReceiptLogTests() => Directory.CreateDirectory(Path.Combine(_root, "integrations"));

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);
    private static string S(byte[] b) => Encoding.UTF8.GetString(b);

    private static IntegrationReceipt Receipt(string folder, IntegrationKind kind = IntegrationKind.Statusline)
        => new() { Folder = folder, File = folder + "/settings.json", Kind = kind };

    [Fact]
    public void Round_trips_with_nothing_set_aside()
    {
        var one = Receipt("/f");
        var data = ReceiptLog.Encode([one])!;
        var back = ReceiptLog.Decode<IntegrationReceipt>(data);
        Assert.Equal(one, Assert.Single(back.Records));
        Assert.Equal(0, back.Skipped);
        Assert.Empty(ReceiptLog.Decode<IntegrationReceipt>(B("[]")).Records);
    }

    [Fact]
    public void Writes_the_macs_field_names_sorted_and_omits_nulls()
    {
        var r = new IntegrationReceipt
        {
            Folder = "C:\\Users\\me\\.claude", File = "C:\\Users\\me\\.claude\\settings.json", Kind = IntegrationKind.Hooks,
            CreatedFile = true, Mode = StatuslineMode.Combine, EmptyInner = "\n",
            Hooks = new HookReceipt { CreatedEvents = ["Notification", "Stop"], HooksInner = " " },
            Switched = new SwitchReceipt { Key = "sanduhr-meters@inline", Value = false, CreatedParent = true },
        };
        r.Hooks!.ArrayInners["Stop"] = "";
        r.Hooks.ArrayInners["Notification"] = "\n  ";
        var text = S(ReceiptJson.Serialize(r));
        using var doc = JsonDocument.Parse(text);
        var names = doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(new[] { "createdFile", "createdParent", "emptyInner", "file", "folder", "hooks", "kind", "mode", "switched" }, names);
        Assert.Equal("hooks", doc.RootElement.GetProperty("kind").GetString());
        Assert.Equal("combine", doc.RootElement.GetProperty("mode").GetString());
        Assert.Equal(new[] { "Notification", "Stop" },
            doc.RootElement.GetProperty("hooks").GetProperty("arrayInners").EnumerateObject().Select(p => p.Name));
        Assert.DoesNotContain("\r", text);

        var back = ReceiptLog.Decode<IntegrationReceipt>(ReceiptLog.Encode([r])!).Records.Single();
        Assert.Equal(r, back);
    }

    [Fact]
    public void Reads_a_receipt_the_mac_wrote_and_a_missing_createdParent_in_a_switch()
    {
        var text = "[{\"createdFile\":false,\"createdParent\":false,\"file\":\"/f/s.json\",\"folder\":\"/f\",\"kind\":\"meters\",\"pinned\":\"20261001\",\"createdKey\":true,\"switched\":{\"key\":\"k@inline\",\"value\":true}}]";
        var d = ReceiptLog.Decode<IntegrationReceipt>(B(text));
        var r = Assert.Single(d.Records);
        Assert.Equal(IntegrationKind.Meters, r.Kind);
        Assert.Equal("20261001", r.Pinned);
        Assert.True(r.CreatedKey);
        Assert.False(r.Switched!.CreatedParent);
    }

    [Fact]
    public void A_malformed_record_is_skipped_and_kept_byte_for_byte_on_save()
    {
        var good = "{\"createdFile\":false,\"createdParent\":false,\"file\":\"/f/settings.json\",\"folder\":\"/f\",\"kind\":\"statusline\"}";
        var future = "{ \"kind\" : \"configEdit\",   \"folder\" : \"/h\", \"keyPath\": \"cleanupPeriodDays\" }";
        var cut = "{\"folder\": \"/g\"}";
        File.WriteAllBytes(Path_, B($"[\n{good},\n{future},\n{cut}\n]\n"));

        var store = new ReceiptStore(Path_);
        var read = store.Load();
        Assert.Single(read.Records);
        Assert.Equal(2, read.Unreadable.Count);
        Assert.Equal(2, read.Skipped);
        Assert.False(read.FileUnreadable);

        var next = new List<IntegrationReceipt>(read.Records) { Receipt("/new", IntegrationKind.Hooks) };
        Assert.True(store.Save(next));
        var text = File.ReadAllText(Path_);
        Assert.Contains(good, text);          // unchanged record: its own bytes
        Assert.Contains(future, text);        // unreadable: byte for byte, spacing and all
        Assert.Contains(cut, text);
        Assert.True(text.IndexOf("/new", StringComparison.Ordinal) < text.IndexOf(future, StringComparison.Ordinal));   // after the others
        var after = store.Load();
        Assert.Equal(new[] { IntegrationKind.Statusline, IntegrationKind.Hooks }, after.Records.Select(r => r.Kind));
        Assert.Equal(new[] { B(future), B(cut) }, after.Unreadable);

        // Removing every readable record keeps the file for the unreadable ones.
        Assert.True(store.Save([]));
        var left = store.Load();
        Assert.Empty(left.Records);
        Assert.Equal(2, left.Unreadable.Count);
    }

    [Fact]
    public void Unknown_fields_survive_a_save_until_the_record_changes()
    {
        var known = "{\"createdFile\":false,\"createdParent\":false,\"file\":\"/f/settings.json\",\"folder\":\"/f\",\"fromTheFuture\":{\"x\":1},\"kind\":\"statusline\"}";
        File.WriteAllBytes(Path_, B($"[\n{known}\n]\n"));
        var store = new ReceiptStore(Path_);
        var receipts = store.Load().Records.ToList();
        receipts.Add(Receipt("/g", IntegrationKind.Hooks));
        store.Save(receipts);
        Assert.Contains(known, File.ReadAllText(Path_));

        receipts = store.Load().Records.ToList();
        receipts[0] = receipts[0] with { CreatedFile = true };
        store.Save(receipts);
        var text = File.ReadAllText(Path_);
        Assert.DoesNotContain("fromTheFuture", text);
        Assert.True(store.Load().Records[0].CreatedFile);
    }

    [Fact]
    public void A_file_that_isnt_an_array_is_moved_aside_never_overwritten()
    {
        File.WriteAllBytes(Path_, B("{not json"));
        var store = new ReceiptStore(Path_);
        var read = store.Load();
        Assert.True(read.FileUnreadable);
        Assert.Equal(1, read.Skipped);

        Assert.True(store.Save([Receipt("/f")]));
        Assert.Single(store.Load().Records);
        Assert.Equal("{not json", File.ReadAllText(ReceiptLog.AsidePath(Path_)));
        Assert.EndsWith("installs.json.unreadable", ReceiptLog.AsidePath(Path_));
    }

    [Fact]
    public void A_second_unreadable_file_never_overwrites_the_first_aside_copy()
    {
        File.WriteAllBytes(Path_, B("{\"first\": true}"));
        var store = new ReceiptStore(Path_);
        store.Save([Receipt("/f")]);

        File.WriteAllBytes(Path_, B("\"second\""));                   // a string at the top level
        store.Save([Receipt("/g")]);

        Assert.Equal("{\"first\": true}", File.ReadAllText(ReceiptLog.AsidePath(Path_)));
        var others = Directory.GetFiles(Path.GetDirectoryName(Path_)!, "installs.json.*.unreadable");
        Assert.Equal("\"second\"", File.ReadAllText(Assert.Single(others)));
        Assert.Equal("/g", store.Load().Records.Single().Folder);
    }

    [Fact]
    public void An_object_at_the_top_level_is_an_unreadable_file()
    {
        Assert.True(ReceiptLog.Decode<IntegrationReceipt>(B("{\"a\": []}")).FileUnreadable);
        Assert.True(ReceiptLog.Decode<IntegrationReceipt>(B("")).FileUnreadable);
        Assert.True(ReceiptLog.Decode<IntegrationReceipt>(B("[1,")).FileUnreadable);
    }

    [Fact]
    public void Non_object_elements_and_null_are_unreadable_records()
    {
        var d = ReceiptLog.Decode<IntegrationReceipt>(B("[5, null, \"x\", {\"folder\":\"/f\",\"file\":\"/f/s\",\"kind\":\"mcp\"}]"));
        Assert.Single(d.Records);
        Assert.Equal(3, d.Unreadable.Count);
        Assert.Equal(B("5"), d.Unreadable[0]);
    }

    [Fact]
    public void Saving_nothing_over_an_absent_or_clean_file_leaves_no_file()
    {
        var store = new ReceiptStore(Path_);
        Assert.True(store.Save([]));
        Assert.False(File.Exists(Path_));

        store.Save([Receipt("/f")]);
        Assert.True(File.Exists(Path_));
        store.Save([]);
        Assert.False(File.Exists(Path_));
        Assert.Empty(store.Load().Records);
    }

    [Fact]
    public void Save_creates_the_folder_and_leaves_no_temp_file()
    {
        var deep = new ReceiptStore(Path.Combine(_root, "a", "b", "installs.json"));
        Assert.True(deep.Save([Receipt("/f")]));
        Assert.Equal(new[] { "installs.json" }, Directory.GetFiles(Path.Combine(_root, "a", "b")).Select(Path.GetFileName));
    }
}
