using Sanduhr.Core;
using Xunit;

namespace Sanduhr.Tests;

public class DrawerJournalTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Written = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);

    private static JournalEntry Entry(string id, DrawerPlanSource source, DateTime at, params JournalMove[] moves) =>
        new(id, at, source, source == DrawerPlanSource.Agent ? "codex" : null, moves);

    private static JournalMove Move(string name) =>
        new($@"C:\Users\pat\Desktop\{name}", $@"C:\Users\pat\Drawers\Documents\{name}", false, Written);

    [Fact]
    public void Encode_and_decode_round_trip()
    {
        var j = new DrawerJournal()
            .Add(Entry("p1", DrawerPlanSource.Agent, Now, Move("a.pdf"), Move("b.pdf")), Now)
            .Add(Entry("u1", DrawerPlanSource.User, Now, new JournalMove(@"C:\Users\pat\Desktop\Proj", @"C:\Users\pat\Drawers\Work\Proj", true, null)), Now);
        var back = DrawerJournal.Decode(j.Encode());
        Assert.Equal(j.Entries.Count, back.Entries.Count);
        Assert.Equal(j.Entries[0].Moves, back.Entries[0].Moves);
        Assert.Equal("codex", back.Entries[0].Agent);
        Assert.Equal(DrawerPlanSource.User, back.Entries[1].Source);
        Assert.Null(back.Entries[1].Moves[0].WrittenUtc);
        Assert.True(back.Entries[1].Moves[0].IsFolder);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"entries\": 5}")]
    [InlineData("{\"entries\": [{\"id\": \"x\"}]}")]
    public void Damaged_journals_read_as_empty_or_keep_what_is_whole(string? json) =>
        Assert.Empty(DrawerJournal.Decode(json).Entries);

    [Fact]
    public void Only_the_last_fifty_plans_are_kept()
    {
        var j = new DrawerJournal();
        for (var i = 0; i < 55; i++) j = j.Add(Entry($"p{i}", DrawerPlanSource.Rules, Now.AddMinutes(i), Move($"{i}.pdf")), Now.AddMinutes(i));
        Assert.Equal(DrawerJournal.MaxPlans, j.Entries.Count);
        Assert.Equal("p5", j.Entries[0].Id);
    }

    [Fact]
    public void User_moves_are_kept_thirty_days_and_do_not_count_toward_the_fifty()
    {
        var j = new DrawerJournal()
            .Add(Entry("old", DrawerPlanSource.User, Now.AddDays(-31), Move("o.pdf")), Now.AddDays(-31))
            .Add(Entry("recent", DrawerPlanSource.User, Now.AddDays(-2), Move("r.pdf")), Now.AddDays(-2));
        for (var i = 0; i < 50; i++) j = j.Add(Entry($"p{i}", DrawerPlanSource.Rules, Now, Move($"{i}.pdf")), Now);
        Assert.DoesNotContain(j.Entries, e => e.Id == "old");
        Assert.Contains(j.Entries, e => e.Id == "recent");
        Assert.Equal(50, j.Entries.Count(e => e.Source != DrawerPlanSource.User));
    }

    [Fact]
    public void The_summary_carries_counts_and_never_paths()
    {
        var j = new DrawerJournal().Add(Entry("p1", DrawerPlanSource.Agent, Now, Move("a.pdf"), Move("b.pdf")), Now);
        var line = Assert.Single(j.Summary());
        Assert.Equal(2, line.Files);
        Assert.Equal("codex", line.Agent);
        Assert.DoesNotContain(typeof(JournalSummaryLine).GetProperties(), p => p.PropertyType == typeof(string) && p.Name != nameof(JournalSummaryLine.Agent));
    }

    [Fact]
    public void Undo_puts_unchanged_files_back_in_reverse_order()
    {
        var e = Entry("p1", DrawerPlanSource.Rules, Now, Move("a.pdf"), Move("b.pdf"));
        var undo = DrawerUndo.Plan(e, _ => Written, _ => false);
        Assert.Empty(undo.Skipped);
        Assert.Equal(
            [new UndoStep(@"C:\Users\pat\Drawers\Documents\b.pdf", @"C:\Users\pat\Desktop\b.pdf"),
             new UndoStep(@"C:\Users\pat\Drawers\Documents\a.pdf", @"C:\Users\pat\Desktop\a.pdf")],
            undo.Steps);
    }

    [Fact]
    public void Undo_skips_a_file_moved_or_changed_since()
    {
        var e = Entry("p1", DrawerPlanSource.Rules, Now, Move("gone.pdf"), Move("edited.pdf"));
        var undo = DrawerUndo.Plan(e, p => p.EndsWith("edited.pdf") ? Written.AddHours(1) : null, _ => false);
        Assert.Empty(undo.Steps);
        Assert.Equal([DrawerUndo.ChangedSince, DrawerUndo.MovedSince], undo.Skipped.Select(s => s.Reason));
    }

    [Fact]
    public void Undo_never_overwrites_what_took_the_old_place()
    {
        var e = Entry("p1", DrawerPlanSource.Rules, Now, Move("a.pdf"));
        var undo = DrawerUndo.Plan(e, _ => Written, p => p == @"C:\Users\pat\Desktop\a.pdf");
        Assert.Equal(@"C:\Users\pat\Desktop\a (2).pdf", Assert.Single(undo.Steps).To);
    }

    [Fact]
    public void Undo_moves_a_folder_back_without_comparing_its_time()
    {
        var e = Entry("u1", DrawerPlanSource.User, Now, new JournalMove(@"C:\Users\pat\Desktop\Proj", @"C:\Users\pat\Drawers\Work\Proj", true, null));
        var undo = DrawerUndo.Plan(e, _ => Written.AddDays(3), _ => false);
        Assert.Single(undo.Steps);
    }
}
