using System.Text;
using Sanduhr.Core;

namespace Sanduhr.Tests;

/// <summary>
/// Ported from the Mac's <c>MessageEditorModelTests</c> in MessageEditorTests.swift, on temp files.
/// Skipped: <c>debugStateIsCountsOnly</c> and <c>debugActionParses</c> (the Mac's state.yaml and debug
/// link). The Mac's <c>debugAdd</c> is replaced by adding the same acceptance line through
/// <see cref="MessageEditorState.AddLineAtTop"/>.
/// </summary>
public sealed class MessageEditorStateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "msg-editor-" + Guid.NewGuid().ToString("N"));

    public MessageEditorStateTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private string TempFile(string text)
    {
        string path = Path.Combine(_dir, "messages.txt");
        File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(text));
        return path;
    }

    private static string Read(string path) => new UTF8Encoding(false, true).GetString(File.ReadAllBytes(path));

    private static MessageLine AcceptanceLine()
    {
        var look = new MessageLook { Ink = MessagePalette.Colors("sunset"), Font = LetterStyle.Script }.SetMotion(MessageMotionKind.Sweep);
        return new MessageLine { When = MessageWhen.OnWeekday("Fri"), Text = "ship it.", Look = look };
    }

    [Fact]
    public void UnchangedFile_SavesBackByteForByte()
    {
        string path = TempFile(MessageDocumentTests.HandWritten);
        var editor = new MessageEditorState(path);
        editor.Load();
        Assert.False(editor.Unsaved);
        editor.ShowText();
        editor.ShowList();
        Assert.False(editor.Unsaved);
        Assert.True(editor.Save());
        Assert.Equal(MessageDocumentTests.HandWritten, Read(path));
    }

    [Fact]
    public void SwitchingViews_KeepsUnsavedEdits()
    {
        string path = TempFile(MessageDocumentTests.HandWritten);
        var editor = new MessageEditorState(path);
        editor.Load();
        var added = AcceptanceLine();
        editor.AddLineAtTop(added);
        Assert.True(editor.Unsaved);
        const string written = "Fri: {ink:#ff7e5f,#feb47b,#ffd86f} {font:script} {sweep} ship it.";
        Assert.Equal(written, added.Written);
        editor.ShowText();
        // Add Line puts it at the top of its day's lines: before the first Friday line.
        string withFriday = MessageDocumentTests.HandWritten.Replace("\nFri:{ink:#FF2A6D", "\n" + written + "\nFri:{ink:#FF2A6D");
        Assert.Equal(withFriday, editor.Text);
        editor.Text += "\n{blink} typed by hand";
        editor.ShowList();
        var last = editor.Document.MessageRows[^1];
        Assert.Equal(MessageContent.Raw("{blink} typed by hand"), last.Content);
        Assert.True(editor.Save());
        Assert.Equal(withFriday + "\n{blink} typed by hand", Read(path));
        Assert.False(editor.Unsaved);
    }

    [Fact]
    public void Revert_DropsEdits_AndSavedRowsReadAsSaved()
    {
        string path = TempFile("a\n");
        var editor = new MessageEditorState(path);
        editor.Load();
        editor.AddLine(new MessageLine { Text = "b" });
        Assert.True(editor.Unsaved);
        editor.Load();
        Assert.False(editor.Unsaved);
        Assert.Single(editor.Document.MessageRows);
        editor.AddLine(new MessageLine { Text = "b" });
        Assert.True(editor.Save());
        Assert.False(editor.Unsaved);
        Assert.Equal("a\nb\n", Read(path));
        var id = editor.Document.MessageRows[1].Id;
        editor.Document.Update(id, l => l with { Look = l.Look with { Glow = false } });
        Assert.True(editor.Unsaved);
        Assert.True(editor.Save());
        Assert.Equal("a\n{noglow} b\n", Read(path));
    }

    [Fact]
    public void SavingFromTheTextView_ReadsTheTextBackIntoRows()
    {
        string path = TempFile("a\n");
        var editor = new MessageEditorState(path);
        editor.Load();
        editor.ShowText();
        editor.Text = "x\nMon: {glow} y\n";
        Assert.True(editor.Unsaved);
        Assert.True(editor.Save());
        Assert.Equal("x\nMon: {glow} y\n", Read(path));
        Assert.Equal(2, editor.Document.MessageRows.Count);
        Assert.False(editor.Unsaved);
    }

    [Fact]
    public void AddLine_OpensTheNewRow_AndLoadClearsIt()
    {
        var editor = new MessageEditorState(TempFile("a\n"));
        editor.Load();
        editor.AddLine(new MessageLine { Text = "b" });
        Assert.NotNull(editor.Expanded);
        Assert.Equal(editor.Expanded, editor.JustAdded);
        editor.FileChanged = true;
        editor.Load();
        Assert.Null(editor.Expanded);
        Assert.Null(editor.JustAdded);
        Assert.False(editor.FileChanged);
    }

    [Fact]
    public void AddLineAtTop_FromTheTextView_ReturnsToTheList()
    {
        var editor = new MessageEditorState(TempFile("a\n"));
        editor.Load();
        editor.ShowText();
        editor.Text = "a\nb\n";
        editor.AddLineAtTop(new MessageLine { Text = "c" });
        Assert.Equal(MessageEditorMode.List, editor.Mode);
        Assert.Equal("c\na\nb\n", editor.CurrentText);
    }

    [Fact]
    public void AddLineAtTop_LoadsFirstWhenNothingWasRead()
    {
        var editor = new MessageEditorState(TempFile("a\n"));
        editor.AddLineAtTop(new MessageLine { Text = "c" });
        Assert.True(editor.Loaded);
        Assert.Equal("c\na\n", editor.CurrentText);
    }

    // Windows-specific

    [Fact]
    public void Save_IsRefusedAfterReset()
    {
        string path = TempFile("a\n");
        bool canWrite = true;
        var editor = new MessageEditorState(path, () => canWrite);
        editor.Load();
        editor.AddLine(new MessageLine { Text = "b" });
        canWrite = false;
        Assert.False(editor.Save());
        Assert.Equal("a\n", Read(path));
        Assert.True(editor.Unsaved);
        canWrite = true;
        Assert.True(editor.Save());
        Assert.Equal("a\nb\n", Read(path));
    }

    [Fact]
    public void EnsureFile_WritesTheStarterOnlyWhenMissing()
    {
        string path = Path.Combine(_dir, "sub", "messages.txt");
        MessageEditorState.EnsureFile(path, () => false);
        Assert.False(File.Exists(path));
        MessageEditorState.EnsureFile(path);
        Assert.Equal(DeskMessages.Starter + "\n", Read(path));
        File.WriteAllText(path, "mine\n");
        MessageEditorState.EnsureFile(path);
        Assert.Equal("mine\n", Read(path));
    }

    [Fact]
    public void Load_CreatesTheStarter_WhenAskedAndMissing()
    {
        string path = Path.Combine(_dir, "messages.txt");
        var editor = new MessageEditorState(path, ensureStarter: true);
        editor.Load();
        Assert.Equal(DeskMessages.Starter + "\n", editor.SavedText);
        Assert.Equal(3, editor.Document.MessageRows.Count);
        Assert.False(editor.Unsaved);
        // Without the flag a missing file reads as empty and nothing is created.
        string other = Path.Combine(_dir, "other.txt");
        var plain = new MessageEditorState(other);
        plain.Load();
        Assert.Equal("", plain.SavedText);
        Assert.False(File.Exists(other));
    }

    [Fact]
    public void Save_WritesUtf8WithoutABom_AndKeepsOneThatWasRead()
    {
        string path = Path.Combine(_dir, "messages.txt");
        var editor = new MessageEditorState(path);
        editor.Load();
        editor.AddLine(new MessageLine { Text = "ünï ✨" });
        Assert.True(editor.Save());
        var bytes = File.ReadAllBytes(path);
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Equal(new UTF8Encoding(false).GetBytes("ünï ✨\n"), bytes);

        // A file that starts with a BOM comes back with it, byte for byte.
        byte[] withBom = [0xEF, 0xBB, 0xBF, .. Encoding.ASCII.GetBytes("hi\n")];
        File.WriteAllBytes(path, withBom);
        editor.Load();
        Assert.False(editor.Unsaved);
        Assert.True(editor.Save());
        Assert.Equal(withBom, File.ReadAllBytes(path));
    }

    [Fact]
    public void Save_LeavesNoTempFileBehind()
    {
        string path = TempFile("a\n");
        var editor = new MessageEditorState(path);
        editor.Load();
        editor.AddLine(new MessageLine { Text = "b" });
        Assert.True(editor.Save());
        Assert.Equal([path], Directory.GetFiles(_dir));
    }

    [Fact]
    public void ANonUtf8File_ReadsAsEmpty()
    {
        string path = Path.Combine(_dir, "messages.txt");
        File.WriteAllBytes(path, [0xFF, 0xFE, 0x41]);
        var editor = new MessageEditorState(path);
        editor.Load();
        Assert.Equal("", editor.SavedText);
        Assert.Empty(editor.Document.Rows);
    }
}
