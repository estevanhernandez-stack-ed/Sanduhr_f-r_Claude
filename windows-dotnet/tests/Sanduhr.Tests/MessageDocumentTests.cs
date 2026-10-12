using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>
/// Ported from the Mac's MessageEditorTests.swift: messages.txt read into rows and written back byte
/// for byte, each control's tag, the When menu's prefixes, the row edits and each row's note. Made-up
/// lines only. Skipped: <c>todayLine</c> (a SwiftUI bar's text) and the debug-state and debug-link cases
/// (the Mac's debug hooks); the model cases that need a file are in MessageEditorStateTests.
/// </summary>
public class MessageDocumentTests
{
    /// <summary>A hand-written file: notes, blank lines, prefixes, every effect, unknown tags, odd spacing.</summary>
    internal static readonly string HandWritten = string.Join("\n",
    [
        "# my lines",
        "keep building.",
        "",
        "  Mon:   one thing at a time.",
        "Fri:{ink:#FF2A6D,#05d9e8}  {glow}   showtime.",
        "10-31: {ink:#ff7518,#6b2fa0} {write} boo.",
        "{noglow} {size:1.30} {font:Small Caps} quiet.",
        "{shimmer} {size:0.5} small shimmer.",
        "{sweep:20} {font:fraktur} every twenty.",
        "{sweep} once.",
        "{blink} unknown tag stays.",
        "{write} {shimmer} two motions stay.",
        "13-01: not a date.",
        "Mon:",
        "{glow} {write}",
        "Note: a colon in a plain line.",
        "\ttabbed line.",
        "   # indented note",
        "{ink:fff} trailing spaces.",
    ]);

    private static string KindOf(MessageRow r) => r.Content.Kind switch
    {
        MessageContentKind.Line => "line",
        MessageContentKind.Raw => "raw",
        MessageContentKind.Comment => "comment",
        _ => "blank",
    };

    private static MessageLine Line(string text, MessageWhen? when = null) => new() { When = when ?? MessageWhen.EveryDay, Text = text };

    [Fact]
    public void DefaultFile_RoundTrips()
    {
        string text = DeskMessages.Starter + "\n";
        var doc = MessageDocument.Parse(text);
        Assert.Equal(text, doc.Text);
        Assert.Equal(4, doc.NoteCount);
        Assert.Equal(3, doc.MessageRows.Count);
        Assert.All(doc.MessageRows, r => Assert.NotNull(r.Line));
        Assert.Equal([MessageWhen.EveryDay, MessageWhen.OnWeekday("Mon"), MessageWhen.OnWeekday("Fri")], doc.MessageRows.Select(r => r.Line!.When));
    }

    [Fact]
    public void HandWrittenFile_RoundTrips()
    {
        foreach (var text in new[] { HandWritten, HandWritten + "\n", HandWritten + "\n\n" })
            Assert.Equal(text, MessageDocument.Parse(text).Text);
    }

    [Fact]
    public void CrlfEmptyAndOneLine_RoundTrip()
    {
        foreach (var text in new[] { "", "\n", "a", "a\r\nb\r\n", "# x\r\n\r\nMon: hi\r\n", "x\n\n\n", "﻿hi\n", "ünï ✨\n" })
            Assert.Equal(text, MessageDocument.Parse(text).Text);
    }

    [Fact]
    public void HandWrittenRows_AreWhatTheDeskReads()
    {
        var rows = MessageDocument.Parse(HandWritten).Rows;
        Assert.Equal(
            ["comment", "line", "blank", "line", "line", "line", "line", "line", "line", "line",
             "raw", "raw", "raw", "raw", "raw", "line", "line", "comment", "line"],
            rows.Select(KindOf));
        var lines = rows.Select(r => r.Line).Where(l => l is not null).Select(l => l!).ToList();
        Assert.Equal(Line("one thing at a time.", MessageWhen.OnWeekday("Mon")), lines[1]);
        Assert.Equal(MessageWhen.OnWeekday("Fri"), lines[2].When);
        Assert.Equal(["ff2a6d", "05d9e8"], lines[2].Look.Ink);
        Assert.True(lines[2].Look.Glow);
        Assert.Equal(MessageWhen.OnDate(10, 31), lines[3].When);
        Assert.Equal(MessageMotionKind.Write, lines[3].Look.Motion);
        Assert.Equal(new MessageLook { Glow = false, Size = 1.3, Font = LetterStyle.SmallCaps }, lines[4].Look);
        Assert.Equal(MessageMotionKind.Shimmer, lines[5].Look.Motion);
        Assert.Equal(0.5, lines[5].Look.Size);
        Assert.Equal(MessageMotionKind.Sweep, lines[6].Look.Motion);
        Assert.Equal(20, lines[6].Look.SweepPeriod);
        Assert.Equal(LetterStyle.Fraktur, lines[6].Look.Font);
        Assert.Null(lines[7].Look.SweepPeriod);
        Assert.Equal("Note: a colon in a plain line.", lines[8].Text);
        Assert.Equal(MessageWhen.EveryDay, lines[8].When);
        Assert.Equal("tabbed line.", lines[9].Text);
        Assert.Equal("trailing spaces.", lines[10].Text);
    }

    [Fact]
    public void LinesTheEditorCannotSet_StayRaw()
    {
        foreach (var line in new[]
        {
            "{blink} hi", "{write} {shimmer} two", "{sweep} {write} two", "13-01: x", "02-30: x", "Mon:   ",
            "{glow} {write}", "{ink:#zz} x", "{size:3} big", "{glow hi", "{font:gothic} x",
        })
        {
            var content = MessageLineModel.ContentOf(line);
            Assert.True(content.Kind == MessageContentKind.Raw, $"{line} should stay raw");
            Assert.Equal(line, content.Text);
        }
    }

    [Fact]
    public void UnchangedRows_WriteTheirSource_EvenAfterAnEditIsUndone()
    {
        var doc = MessageDocument.Parse(HandWritten);
        var id = doc.Rows[4].Id;
        var before = doc.Rows[4].Line!;
        doc.Update(id, l => l with { Look = l.Look with { Glow = null } });
        Assert.NotEqual(HandWritten, doc.Text);
        doc.Update(id, _ => before);
        Assert.Equal(HandWritten, doc.Text);
    }

    [Fact]
    public void AChangedRow_RewritesOnlyItsLine()
    {
        var doc = MessageDocument.Parse(HandWritten);
        doc.Update(doc.Rows[4].Id, l => l with { Text = "curtain up." });
        var old = HandWritten.Split('\n');
        var now = doc.Text.Split('\n');
        Assert.Equal(old.Length, now.Length);
        for (int i = 0; i < old.Length; i++)
            if (i != 4) Assert.Equal(old[i], now[i]);
        Assert.Equal("Fri: {ink:#ff2a6d,#05d9e8} {glow} curtain up.", now[4]);
    }

    [Fact]
    public void AChangedCrlfRow_KeepsItsEnding()
    {
        var doc = MessageDocument.Parse("# n\r\nhi\r\n");
        doc.Update(doc.Rows[1].Id, l => l with { Look = l.Look with { Glow = true } });
        Assert.Equal("# n\r\n{glow} hi\r\n", doc.Text);
    }

    // Controls

    private static string? Written(Func<MessageLine, MessageLine> change) => change(Line("hi")).Written;

    [Fact]
    public void TheAcceptanceLine()
    {
        var line = new MessageLine { When = MessageWhen.OnWeekday("Fri"), Text = "ship it." };
        line = line with { Look = (line.Look with { Ink = MessagePalette.Colors("sunset"), Font = LetterStyle.Script }).SetMotion(MessageMotionKind.Sweep) };
        Assert.Equal("Fri: {ink:#ff7e5f,#feb47b,#ffd86f} {font:script} {sweep} ship it.", line.Written);
        Assert.Equal(MessageContent.FromLine(line), MessageLineModel.ContentOf(line.Written!));
    }

    [Fact]
    public void WhenPrefixes()
    {
        Assert.Equal("", MessageWhen.EveryDay.Prefix);
        Assert.Equal(["Mon: ", "Tue: ", "Wed: ", "Thu: ", "Fri: ", "Sat: ", "Sun: "], MessageWhen.Weekdays.Select(d => MessageWhen.OnWeekday(d).Prefix));
        Assert.Equal("10-31: ", MessageWhen.OnDate(10, 31).Prefix);
        Assert.Equal("02-05: ", MessageWhen.OnDate(2, 5).Prefix);
        Assert.Equal("02-29: ", MessageWhen.OnDate(2, 29).Prefix);
        // Every weekday the menu writes is one the Desk reads.
        Assert.Equal(MessageWhen.Weekdays.ToHashSet(), DeskMessages.Weekdays.ToHashSet());
        foreach (var when in new[] { MessageWhen.EveryDay, MessageWhen.OnWeekday("Sun"), MessageWhen.OnDate(12, 25), MessageWhen.OnDate(1, 1) })
        {
            var line = Line("x", when);
            Assert.Equal(MessageContent.FromLine(line), MessageLineModel.ContentOf(line.Written!));
        }
        Assert.Equal("Fridays", MessageWhen.OnWeekday("Fri").Label);
        Assert.Equal("October 31", MessageWhen.OnDate(10, 31).Label);
        Assert.Equal("Every day", MessageWhen.EveryDay.Label);
        Assert.Equal(29, MessageWhen.DaysInMonth(2));
        Assert.Equal(30, MessageWhen.DaysInMonth(4));
        Assert.Null(MessageWhen.ParseDate("13-01"));
        Assert.Null(MessageWhen.ParseDate("02-30"));
        Assert.Equal(MessageWhen.OnDate(10, 31), MessageWhen.ParseDate("10-31"));
    }

    [Fact]
    public void Color()
    {
        Assert.Equal("hi", Written(l => l));
        Assert.Equal("{ink:#ff2a6d} hi", Written(l => l with { Look = l.Look.SetInkMode(MessageInkMode.Solid) }));
        Assert.Equal("{ink:#ff2a6d,#05d9e8} hi", Written(l => l with { Look = l.Look.SetInkMode(MessageInkMode.Gradient) }));
        Assert.Equal("{ink:#ff2a6d,#05d9e8,#05d9e8} hi", Written(l => l with { Look = l.Look.SetInkMode(MessageInkMode.Gradient).AddStop() }));
        Assert.Equal("{ink:#111111,#222222,#333333,#444444} hi", Written(l => l with { Look = (l.Look with { Ink = ["111111", "222222", "333333", "444444"] }).AddStop() }));
        Assert.Equal("{ink:#111111,#333333} hi", Written(l => l with { Look = (l.Look with { Ink = ["111111", "222222", "333333"] }).RemoveStop(1).RemoveStop(0).RemoveStop(0) }));
        Assert.Equal("{ink:#abcdef} hi", Written(l => l with { Look = l.Look.SetInkMode(MessageInkMode.Solid).SetStop(0, "#ABCDEF") }));
        Assert.Equal("{ink:#ff2a6d} hi", Written(l => l with { Look = l.Look.SetInkMode(MessageInkMode.Solid).SetStop(0, "nope") }));
        Assert.Equal("hi", Written(l => l with { Look = l.Look.SetInkMode(MessageInkMode.Gradient).SetInkMode(MessageInkMode.Desk) }));
        Assert.Equal(MessageInkMode.Desk, new MessageLook().InkMode);
        Assert.Equal(MessageInkMode.Solid, new MessageLook { Ink = ["fff"] }.InkMode);
        Assert.Equal(MessageInkMode.Gradient, new MessageLook { Ink = ["fff", "000"] }.InkMode);
        Assert.Equal(["As the Desk", "One color", "Gradient"], new[] { MessageInkMode.Desk, MessageInkMode.Solid, MessageInkMode.Gradient }.Select(MessageLook.InkModeTitle));
    }

    [Fact]
    public void Palettes()
    {
        Assert.Equal(["synthwave", "sunset", "ocean", "aurora", "ember", "bubblegum", "toxic", "gold"], MessagePalette.All.Select(p => p.Name));
        Assert.Equal("{ink:#ff2a6d,#d16ba5,#05d9e8} hi", Written(l => l with { Look = l.Look with { Ink = MessagePalette.Colors("synthwave") } }));
        Assert.Equal("{ink:#f7971e,#ffd200,#fff6b7} hi", Written(l => l with { Look = l.Look with { Ink = MessagePalette.Colors("gold") } }));
        Assert.Equal("aurora", MessagePalette.NameOf(["00F5A0", "00d9f5", "a06bff"]));
        Assert.Null(MessagePalette.NameOf(["000000"]));
        Assert.Null(MessagePalette.NameOf(null));
        Assert.Null(MessagePalette.Colors("nope"));
        foreach (var p in MessagePalette.All)
        {
            Assert.Equal(3, p.Colors.Count);
            Assert.All(p.Colors, c => Assert.True(MessageMarkup.IsHex(c)));
        }
    }

    [Fact]
    public void GlowSizeLettersMotion()
    {
        Assert.Equal("{glow} hi", Written(l => l with { Look = l.Look with { Glow = true } }));
        Assert.Equal("{noglow} hi", Written(l => l with { Look = l.Look with { Glow = false } }));
        Assert.Equal("{size:1.25} hi", Written(l => l with { Look = l.Look with { Size = 1.25 } }));
        Assert.Equal("{size:0.5} hi", Written(l => l with { Look = l.Look with { Size = 0.5 } }));
        Assert.Equal("{size:2} hi", Written(l => l with { Look = l.Look with { Size = 2 } }));
        Assert.Equal("hi", Written(l => l with { Look = l.Look with { Size = 1 } }));
        foreach (var style in LetterStyles.All)
            Assert.Equal($"{{font:{LetterStyles.Word(style)}}} hi", Written(l => l with { Look = l.Look with { Font = style } }));
        Assert.Equal(LetterStyles.All.ToHashSet(), MessageLetters.Order.ToHashSet());
        Assert.Equal("{write} hi", Written(l => l with { Look = l.Look.SetMotion(MessageMotionKind.Write) }));
        Assert.Equal("{shimmer} hi", Written(l => l with { Look = l.Look.SetMotion(MessageMotionKind.Shimmer) }));
        Assert.Equal("{sweep} hi", Written(l => l with { Look = l.Look.SetMotion(MessageMotionKind.Sweep) }));
        Assert.Equal(["None", "Write in", "Shimmer", "Sweep"], MessageMotionKinds.All.Select(MessageMotionKinds.Title));
        // A {sweep:20} keeps its period while Motion stays Sweep, and drops it on a change.
        var line = Line("hi");
        line = line with { Look = line.Look with { Motion = MessageMotionKind.Sweep, SweepPeriod = 20 } };
        Assert.Equal("{sweep:20} hi", line.Written);
        line = line with { Look = line.Look.SetMotion(MessageMotionKind.Sweep) };
        Assert.Equal("{sweep:20} hi", line.Written);
        line = line with { Look = line.Look.SetMotion(MessageMotionKind.None) };
        line = line with { Look = line.Look.SetMotion(MessageMotionKind.Sweep) };
        Assert.Equal("{sweep} hi", line.Written);
    }

    [Fact]
    public void EveryLook_ReadsBack()
    {
        var line = new MessageLine
        {
            When = MessageWhen.OnDate(3, 14),
            Text = "pi day.",
            Look = new MessageLook { Ink = ["abc", "123456"], Glow = false, Size = 1.75, Font = LetterStyle.DoubleStruck, Motion = MessageMotionKind.Write },
        };
        Assert.Equal("03-14: {ink:#abc,#123456} {noglow} {size:1.75} {font:double-struck} {write} pi day.", line.Written);
        Assert.Equal(MessageContent.FromLine(line), MessageLineModel.ContentOf(line.Written!));
        // The Desk reads the tags as the editor meant them.
        var parsed = MessageMarkup.Parse(line.Body);
        Assert.Equal("pi day.", parsed.Text);
        Assert.Equal(1.75, parsed.Effects.Size);
        Assert.Equal(LetterStyle.DoubleStruck, parsed.Effects.Font);
        Assert.True(parsed.Effects.Write);
    }

    [Fact]
    public void Text_StaysOnOneLine()
    {
        Assert.Equal("two lines", Line("two\nlines").Written);
        Assert.Equal("two lines", Line("two\r\nlines").Written);
        Assert.Equal("padded", Line("  padded  ").Written);
        Assert.Null(Line("   ").Written);
    }

    [Fact]
    public void Problems_AreNamed()
    {
        Assert.Null(Line("fine.").Problem);
        Assert.Contains("note", Line("#1 dad").Problem);
        Assert.Null((Line("#1 dad") with { Look = new MessageLook { Glow = true } }).Problem);
        Assert.Contains("{", Line("{glow} hi").Problem);
        Assert.Contains("day", Line("Mon: hi").Problem);
        Assert.Null(Line("Mon: hi", MessageWhen.OnWeekday("Tue")).Problem);
        Assert.Null(Line("").Problem);
    }

    [Fact]
    public void Number_HasAtMostTwoDecimalsAndNoTrailingZeros()
    {
        Assert.Equal("1.25", MessageLook.Number(1.25));
        Assert.Equal("0.5", MessageLook.Number(0.5));
        Assert.Equal("2", MessageLook.Number(2));
        Assert.Equal("1.3", MessageLook.Number(1.3));
        Assert.Equal("1.33", MessageLook.Number(1.3333));
        Assert.Equal("20", MessageLook.Number(20));
    }

    // Row edits

    [Fact]
    public void Add_Appends_AndAnEmptyLineIsLeftOut()
    {
        var doc = MessageDocument.Parse(HandWritten);
        doc.Add();
        Assert.Equal(HandWritten, doc.Text);
        var empty = MessageDocument.Parse("");
        empty.Add(Line("first."));
        Assert.Equal("first.\n", empty.Text);
        var noNewline = MessageDocument.Parse("a");
        noNewline.Add(Line("b"));
        Assert.Equal("a\nb", noNewline.Text);
    }

    [Fact]
    public void Duplicate_CopiesTheLineAsWritten()
    {
        var doc = MessageDocument.Parse(HandWritten);
        var source = doc.Rows[4];
        var copy = doc.Duplicate(source.Id);
        Assert.NotNull(copy);
        Assert.NotEqual(source.Id, copy);
        var lines = doc.Text.Split('\n');
        var old = HandWritten.Split('\n');
        Assert.Equal(old.Length + 1, lines.Length);
        Assert.Equal(old[4], lines[4]);
        Assert.Equal(old[4], lines[5]);
        Assert.Equal(old[5..], lines[6..]);
        Assert.Equal(old[..4], lines[..4]);
        Assert.Null(doc.Duplicate(Guid.NewGuid()));
    }

    [Fact]
    public void Delete_RemovesOnlyThatLine()
    {
        var doc = MessageDocument.Parse(HandWritten);
        doc.Delete(doc.Rows[10].Id);
        var old = HandWritten.Split('\n').ToList();
        old.RemoveAt(10);
        Assert.Equal(string.Join("\n", old), doc.Text);
    }

    [Fact]
    public void Drag_MovesOneLineAndKeepsTheRest()
    {
        var old = HandWritten.Split('\n').ToList();
        // Down: after the target.
        var doc = MessageDocument.Parse(HandWritten);
        doc.Move(doc.Rows[1].Id, doc.Rows[5].Id);
        var want = old.ToList();
        var moved = want[1];
        want.RemoveAt(1);
        want.Insert(5, moved);
        Assert.Equal(string.Join("\n", want), doc.Text);
        // Up: before the target.
        var up = MessageDocument.Parse(HandWritten);
        up.Move(up.Rows[9].Id, up.Rows[3].Id);
        var wantUp = old.ToList();
        var m = wantUp[9];
        wantUp.RemoveAt(9);
        wantUp.Insert(3, m);
        Assert.Equal(string.Join("\n", wantUp), up.Text);
        // Onto itself: nothing.
        var same = MessageDocument.Parse(HandWritten);
        same.Move(same.Rows[4].Id, same.Rows[4].Id);
        Assert.Equal(HandWritten, same.Text);
    }

    [Fact]
    public void MoveUpAndDown_StepPastMessageRowsOnly()
    {
        var doc = MessageDocument.Parse("# top\nA\n\nB\n# mid\nC\n");
        var b = doc.Rows[3].Id;
        doc.MoveBy(b, -1);
        Assert.Equal("# top\nB\nA\n\n# mid\nC\n", doc.Text);
        doc.MoveBy(b, -1);
        Assert.Equal("# top\nB\nA\n\n# mid\nC\n", doc.Text);
        var a = doc.Rows[2].Id;
        doc.MoveBy(a, 1);
        Assert.Equal("# top\nB\n\n# mid\nC\nA\n", doc.Text);
        doc.MoveBy(a, 1);
        Assert.Equal("# top\nB\n\n# mid\nC\nA\n", doc.Text);
    }

    [Fact]
    public void Rebase_MakesWrittenLinesTheSource()
    {
        var doc = MessageDocument.Parse("a\n");
        doc.Update(doc.Rows[0].Id, l => l with { Look = l.Look with { Glow = true } });
        doc.Add();
        doc.Rebase();
        Assert.Single(doc.Rows);
        Assert.Equal("{glow} a", doc.Rows[0].Source);
        Assert.Equal("{glow} a\n", doc.Text);
    }

    [Fact]
    public void Rebase_KeepsCrlfRowsAndTheirIds()
    {
        var doc = MessageDocument.Parse("# n\r\nhi\r\n");
        var id = doc.Rows[1].Id;
        doc.Update(id, l => l with { Look = l.Look with { Glow = true } });
        doc.Rebase();
        Assert.Equal(id, doc.Rows[1].Id);
        Assert.Equal("{glow} hi", doc.Rows[1].Source);
        Assert.True(doc.Rows[1].Crlf);
        Assert.Equal("# n\r\n{glow} hi\r\n", doc.Text);
    }

    [Fact]
    public void InsertAtTopOfGroup_GoesBeforeTheFirstRowOfTheSameDay()
    {
        var doc = MessageDocument.Parse("# top\nkeep.\nMon: a\nFri: b\nFri: c\n");
        doc.InsertAtTopOfGroup(Line("new fri", MessageWhen.OnWeekday("Fri")));
        Assert.Equal("# top\nkeep.\nMon: a\nFri: new fri\nFri: b\nFri: c\n", doc.Text);
        // No row for that day: before the first message row.
        doc.InsertAtTopOfGroup(Line("new tue", MessageWhen.OnWeekday("Tue")));
        Assert.Equal("# top\nTue: new tue\nkeep.\nMon: a\nFri: new fri\nFri: b\nFri: c\n", doc.Text);
        // No message rows: after the last row.
        var notes = MessageDocument.Parse("# only\n\n");
        notes.InsertAtTopOfGroup(Line("first"));
        Assert.Equal("# only\n\nfirst\n", notes.Text);
        var empty = new MessageDocument();
        empty.InsertAtTopOfGroup(Line("x"));
        Assert.Equal("x\n", empty.Text);
    }

    [Fact]
    public void InsertAtTopOfGroup_MatchesARawRowsDay()
    {
        var doc = MessageDocument.Parse("{blink} odd\nFri: b\n");
        doc.InsertAtTopOfGroup(Line("x"));
        Assert.Equal("x\n{blink} odd\nFri: b\n", doc.Text);
    }

    [Fact]
    public void Document_Equality_AndClone()
    {
        var a = MessageDocument.Parse(HandWritten);
        var copy = a.Clone();
        Assert.Equal(a, copy);
        copy.Delete(copy.Rows[0].Id);
        Assert.NotEqual(a, copy);
        Assert.Equal(HandWritten, a.Text);
        Assert.NotEqual(MessageDocument.Parse("a\n"), MessageDocument.Parse("a"));
    }

    // Notes

    private static IReadOnlyList<string> Notes(string text, bool mix = false)
    {
        var doc = MessageDocument.Parse(text);
        return doc.MessageRows.Select(r => doc.Note(r, mix)).ToList();
    }

    [Fact]
    public void Notes_DateRows()
    {
        Assert.Equal(["October 31 · shows above the day's message"], Notes("10-31: boo."));
        Assert.Equal(
        [
            "March 14 · shows above the day's message · with 1 other that day",
            "March 14 · shows above the day's message · with 1 other that day",
            "April 1 · shows above the day's message",
        ], Notes("03-14: Sam.\n03-14: Alex.\n04-01: joke."));
        string four = string.Join("\n", Enumerable.Range(1, 4).Select(i => $"03-14: {i}"));
        Assert.Equal("March 14 · shows above the day's message · with 3 others that day · 3 at a time, taking turns hourly", Notes(four)[0]);
    }

    [Fact]
    public void Notes_WeekdayRows()
    {
        Assert.Equal("Fridays · takes turns with 2 others", Notes("Fri: a.\nFri: b.\nFri: c.\nkeep.")[0]);
        Assert.Equal("Fridays · shows instead of the every-day lines", Notes("Fri: a.\nkeep.")[0]);
        Assert.Equal("Fridays · mixes with every-day lines", Notes("Fri: a.\nkeep.", mix: true)[0]);
        Assert.Equal("Fridays · takes turns with 1 other · mixes with every-day lines", Notes("Fri: a.\nFri: b.", mix: true)[0]);
    }

    [Fact]
    public void Notes_EveryDayRows()
    {
        Assert.Equal(["Every day"], Notes("keep."));
        Assert.Equal("Every day · takes turns with 1 other", Notes("keep.\ngo.")[0]);
        Assert.Equal("Every day · steps aside on days with their own line", Notes("keep.\nFri: a.")[0]);
        Assert.Equal("Every day · takes turns with 2 others · steps aside on days with their own line", Notes("keep.\ngo.\nship.\nMon: a.")[0]);
        Assert.Equal("Every day · mixes in on days with their own line", Notes("keep.\nFri: a.", mix: true)[0]);
        // A date line is not a day with its own line: it adds, it doesn't replace.
        Assert.Equal("Every day", Notes("keep.\n10-31: boo.")[0]);
    }

    [Fact]
    public void Notes_RawAndEmptyRows()
    {
        Assert.Equal("Every day · takes turns with 1 other", Notes("{blink} hi\n{blink} ho")[0]);
        Assert.Equal(["As written · never shows"], Notes("13-01: no."));
        var doc = MessageDocument.Parse("keep.");
        doc.Add();
        Assert.Equal("Shows once it has text", doc.Note(doc.Rows[1], false));
        Assert.Equal("Every day", doc.Note(doc.Rows[0], false));
    }
}
