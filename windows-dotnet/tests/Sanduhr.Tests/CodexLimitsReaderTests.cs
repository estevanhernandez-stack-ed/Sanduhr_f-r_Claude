using System.IO.Compression;
using System.Text;
using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class CodexLimitsReaderTests
{
    private static string Event(string ts, double five, double week) =>
        $$$$"""{"timestamp":"{{{{ts}}}}","type":"event_msg","payload":{"type":"token_count","rate_limits":{"limit_id":"codex","primary":{"used_percent":{{{{five}}}},"window_minutes":300,"resets_at":1792721664},"secondary":{"used_percent":{{{{week}}}},"window_minutes":10080,"resets_at":1793308464},"plan_type":"plus"}}}""";
    private const string Premium = """{"timestamp":"2026-10-22T23:59:00Z","payload":{"type":"token_count","rate_limits":{"limit_id":"premium","primary":null,"secondary":null}}}""";

    // Tests use GZip as a stand-in compressor: the reader only sees Func<Stream, Stream>.
    private static Stream Gunzip(Stream s) => new GZipStream(s, CompressionMode.Decompress);

    private static string Rollout(string home, string day, string name, string body, bool compressed = false, DateTime? mtime = null)
    {
        var dir = Path.Combine(home, "sessions", "2026", "10", day);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name + (compressed ? ".jsonl.zst" : ".jsonl"));
        if (compressed)
        {
            using var f = File.Create(path);
            using var gz = new GZipStream(f, CompressionLevel.Fastest);
            gz.Write(Encoding.UTF8.GetBytes(body));
        }
        else File.WriteAllText(path, body);
        if (mtime is { } m) File.SetLastWriteTimeUtc(path, m);
        return path;
    }

    private static CodexLimitsReader Reader(params string[] homes) => new(() => homes, Gunzip);

    [Fact]
    public void No_home_is_not_installed() =>
        Assert.Equal(CodexReadStatus.NotInstalled, Reader().Read().Status);

    [Fact]
    public void A_home_with_no_rollouts_has_no_limits()
    {
        using var t = new TempDir();
        Assert.Equal(CodexReadStatus.NoLimits, Reader(t.Path).Read().Status);
    }

    [Fact]
    public void Skips_a_trailing_premium_event_and_a_torn_last_line()
    {
        using var t = new TempDir();
        Rollout(t.Path, "22", "rollout-a", Event("2026-10-22T23:48:00Z", 40, 10) + "\n" + Premium + "\n" + "{\"timestamp\":\"2026-10-22T23:59:30Z\",\"payl");
        var r = Reader(t.Path).Read();
        Assert.Equal(CodexReadStatus.Ok, r.Status);
        Assert.Equal(40, r.Reading!.Primary!.UsedPercent);
    }

    [Fact]
    public void Reads_compressed_rollouts_through_the_injected_decompressor()
    {
        using var t = new TempDir();
        Rollout(t.Path, "22", "rollout-z", Event("2026-10-22T23:48:00Z", 55, 12) + "\n", compressed: true);
        Assert.Equal(55, Reader(t.Path).Read().Reading!.Primary!.UsedPercent);
    }

    [Fact]
    public void A_compressed_rollout_with_no_decompressor_is_skipped()
    {
        using var t = new TempDir();
        Rollout(t.Path, "22", "rollout-z", Event("2026-10-22T23:48:00Z", 55, 12) + "\n", compressed: true);
        Assert.Equal(CodexReadStatus.NoLimits, new CodexLimitsReader(() => [t.Path], null).Read().Status);
    }

    [Fact]
    public void The_newest_file_across_two_homes_wins()
    {
        using var a = new TempDir();
        using var b = new TempDir();
        Rollout(a.Path, "21", "rollout-old", Event("2026-10-21T10:00:00Z", 10, 1) + "\n", mtime: new DateTime(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc));
        Rollout(b.Path, "22", "rollout-new", Event("2026-10-22T10:00:00Z", 70, 7) + "\n", mtime: new DateTime(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc));
        Assert.Equal(70, Reader(a.Path, b.Path).Read().Reading!.Primary!.UsedPercent);
    }

    [Fact]
    public void A_newest_file_with_no_limits_falls_back_to_the_next()
    {
        using var t = new TempDir();
        Rollout(t.Path, "21", "rollout-old", Event("2026-10-21T10:00:00Z", 33, 3) + "\n", mtime: new DateTime(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc));
        Rollout(t.Path, "22", "rollout-new", Premium + "\n", mtime: new DateTime(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc));
        Assert.Equal(33, Reader(t.Path).Read().Reading!.Primary!.UsedPercent);
    }

    [Fact]
    public void A_decompressor_that_throws_on_every_file_is_unreadable_not_a_crash()
    {
        using var t = new TempDir();
        Rollout(t.Path, "22", "rollout-z", "garbage", compressed: true);
        var r = new CodexLimitsReader(() => [t.Path], _ => throw new InvalidDataException("bad frame")).Read();
        Assert.Equal(CodexReadStatus.Unreadable, r.Status);
    }

    [Fact]
    public void Never_opens_auth_json()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Sanduhr.Core", "CodexLimitsReader.cs"));
        Assert.DoesNotContain("auth.json", source, StringComparison.OrdinalIgnoreCase);

        using var t = new TempDir();
        var newer = Event("2026-10-30T10:00:00Z", 88, 88) + "\n";
        File.WriteAllText(Path.Combine(t.Path, "auth.json"), newer);
        Directory.CreateDirectory(Path.Combine(t.Path, "sessions"));
        File.WriteAllText(Path.Combine(t.Path, "sessions", "auth.json"), newer);
        Rollout(t.Path, "22", "rollout-2026-10-22T10-00-00-a", Event("2026-10-22T10:00:00Z", 40, 10) + "\n");
        Assert.Equal(40, Reader(t.Path).Read().Reading!.Primary!.UsedPercent);
    }

    private static readonly DateTime Oct25 = new(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Newest_by_content_beats_newest_by_mtime()
    {
        using var t = new TempDir();
        // Codex rewrote an old rollout when it compressed it: old name, fresh mtime, old event.
        Rollout(t.Path, "08", "rollout-2026-10-08T10-00-00-old", Event("2026-10-08T10:00:00Z", 25, 4) + "\n", mtime: new DateTime(2026, 10, 22, 18, 47, 0, DateTimeKind.Utc));
        Rollout(t.Path, "22", "rollout-2026-10-22T20-00-00-new", Event("2026-10-22T23:48:09Z", 99, 16) + "\n", mtime: new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc));
        // More than MaxFiles rewritten rollouts with newer mtimes: the new one is only reachable by its name.
        for (int i = 0; i < CodexLimitsReader.MaxFiles + 5; i++)
            Rollout(t.Path, "01", $"rollout-2026-10-01T00-00-{i:00}-f", Premium + "\n", mtime: Oct25.AddMinutes(i));
        var r = Reader(t.Path).Read();
        Assert.Equal(CodexReadStatus.Ok, r.Status);
        Assert.Equal(99, r.Reading!.Primary!.UsedPercent);
    }

    [Fact]
    public void A_live_old_thread_with_the_newest_event_wins()
    {
        using var t = new TempDir();
        Rollout(t.Path, "08", "rollout-2026-10-08T10-00-00-live", Event("2026-10-23T09:00:00Z", 61, 9) + "\n", mtime: Oct25);
        Rollout(t.Path, "22", "rollout-2026-10-22T10-00-00-newer-name", Event("2026-10-22T10:00:00Z", 20, 2) + "\n", mtime: new DateTime(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc));
        Assert.Equal(61, Reader(t.Path).Read().Reading!.Primary!.UsedPercent);
    }

    [Fact]
    public void Unchanged_files_are_not_decompressed_again_and_an_append_is_picked_up()
    {
        using var t = new TempDir();
        Rollout(t.Path, "21", "rollout-2026-10-21T10-00-00-z", Event("2026-10-21T10:00:00Z", 15, 1) + "\n", compressed: true);
        var plain = Rollout(t.Path, "22", "rollout-2026-10-22T10-00-00-p", Event("2026-10-22T10:00:00Z", 30, 3) + "\n");
        int calls = 0;
        var reader = new CodexLimitsReader(() => [t.Path], s => { calls++; return Gunzip(s); });
        Assert.Equal(30, reader.Read().Reading!.Primary!.UsedPercent);
        int afterFirst = calls;
        Assert.Equal(1, afterFirst);
        Assert.Equal(30, reader.Read().Reading!.Primary!.UsedPercent);
        Assert.Equal(afterFirst, calls);
        File.AppendAllText(plain, Event("2026-10-22T11:00:00Z", 45, 4) + "\n");
        Assert.Equal(45, reader.Read().Reading!.Primary!.UsedPercent);
        Assert.Equal(afterFirst, calls);
    }

    private sealed class EndlessZeros : Stream
    {
        public override int Read(byte[] buffer, int offset, int count) { Array.Clear(buffer, offset, count); return count; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public void A_stream_past_the_decompressed_cap_fails_that_file_without_throwing()
    {
        using var t = new TempDir();
        Rollout(t.Path, "22", "rollout-z", "x", compressed: true);
        var r = new CodexLimitsReader(() => [t.Path], _ => new EndlessZeros()).Read();
        Assert.Equal(CodexReadStatus.Unreadable, r.Status);
    }

    [Fact]
    public void A_plain_file_over_the_tail_cap_still_yields_its_last_event()
    {
        using var t = new TempDir();
        var filler = new string('x', 999) + "\n";
        var sb = new StringBuilder();
        for (int i = 0; i < (CodexLimitsReader.MaxTailBytes / 1000) + 500; i++) sb.Append(filler);
        sb.Append(Event("2026-10-22T10:00:00Z", 77, 7)).Append('\n');
        var path = Rollout(t.Path, "22", "rollout-big", sb.ToString());
        Assert.True(new FileInfo(path).Length > CodexLimitsReader.MaxTailBytes);
        Assert.Equal(77, Reader(t.Path).Read().Reading!.Primary!.UsedPercent);
    }

    [Fact]
    public void One_file_failing_does_not_hide_another_files_reading()
    {
        using var t = new TempDir();
        Rollout(t.Path, "21", "rollout-bad", "garbage", compressed: true);
        Rollout(t.Path, "22", "rollout-good", Event("2026-10-22T10:00:00Z", 52, 5) + "\n");
        var r = new CodexLimitsReader(() => [t.Path], _ => throw new InvalidDataException("bad frame")).Read();
        Assert.Equal(CodexReadStatus.Ok, r.Status);
        Assert.Equal(52, r.Reading!.Primary!.UsedPercent);
    }

    [Fact]
    public void One_file_failing_and_another_empty_is_no_limits()
    {
        using var t = new TempDir();
        Rollout(t.Path, "21", "rollout-bad", "garbage", compressed: true);
        Rollout(t.Path, "22", "rollout-empty", Premium + "\n");
        var r = new CodexLimitsReader(() => [t.Path], _ => throw new InvalidDataException("bad frame")).Read();
        Assert.Equal(CodexReadStatus.NoLimits, r.Status);
    }

    [Fact]
    public void Homes_keep_existing_folders_only_with_CODEX_HOME_first()
    {
        using var profile = new TempDir();
        using var custom = new TempDir();
        Directory.CreateDirectory(Path.Combine(profile.Path, ".codex"));
        Assert.Equal(new[] { custom.Path, Path.Combine(profile.Path, ".codex") }, CodexHomes.Discover(custom.Path, profile.Path));
        Assert.Equal(new[] { Path.Combine(profile.Path, ".codex") }, CodexHomes.Discover(Path.Combine(custom.Path, "missing"), profile.Path));
        Assert.Single(CodexHomes.Discover(Path.Combine(profile.Path, ".codex"), profile.Path));
    }
}
