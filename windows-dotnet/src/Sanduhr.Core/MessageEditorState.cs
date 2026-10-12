using System.Text;

namespace Sanduhr.Core;

/// <summary>
/// File reads and writes for the message files. Text is UTF-8 without a BOM on write; on read a BOM
/// is kept as a character (U+FEFF) so a file saves back byte for byte. Writes go through a temp
/// sibling and a replace, so a reader never sees half a file.
/// </summary>
internal static class MessageFileIo
{
    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>The file's text, null when it is missing, unreadable or not UTF-8.</summary>
    public static string? ReadText(string path)
    {
        try
        {
            return Strict.GetString(File.ReadAllBytes(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return null;
        }
    }

    /// <summary>The text as the file holds it (UTF-8, no BOM).</summary>
    public static byte[] Encode(string text) => new UTF8Encoding(false).GetBytes(text);

    /// <summary>
    /// Writes <paramref name="bytes"/> to <paramref name="path"/> through a temp sibling, replacing a
    /// file that is there. Creates the folder. Throws the I/O exception on failure, with no temp left behind.
    /// </summary>
    public static void WriteAtomic(string path, byte[] bytes)
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            File.WriteAllBytes(tmp, bytes);
            if (File.Exists(path))
            {
                try { File.Replace(tmp, path, null); }
                catch (IOException) { File.Move(tmp, path, overwrite: true); }
            }
            else
            {
                File.Move(tmp, path);
            }
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
            throw;
        }
    }

    /// <summary>
    /// Takes a request file: moves it aside (so a second reader cannot get it and the writer's next file
    /// is a new one), reads it and deletes it. Null when there is none or it is still being written.
    /// </summary>
    public static byte[]? Take(string path)
    {
        string claim = path + ".taken";
        try
        {
            if (!File.Exists(path)) return null;
            File.Move(path, claim, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        try
        {
            return File.ReadAllBytes(claim);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            try { File.Delete(claim); } catch { /* best effort */ }
        }
    }
}

/// <summary>Which view of messages.txt the editor shows.</summary>
public enum MessageEditorMode { List, Text }

/// <summary>
/// Settings, Message's editor state, the pure half: the file as rows (the list) or as text (Edit as
/// text), one document either way. Switching views carries every byte across: the list writes its
/// rows to the text, the text is read back into rows. Unsaved means the text the current view would
/// write differs from the file as last read or saved. Ported from the Mac's Desk/MessageEditorModel.swift
/// without the debug hooks and the observation plumbing; the view layer owns notification.
/// </summary>
public sealed class MessageEditorState
{
    private readonly string _path;
    private readonly Func<bool> _canWrite;
    private readonly bool _ensureStarter;

    /// <param name="path">messages.txt.</param>
    /// <param name="canWrite">False once Reset has run: nothing is written then.</param>
    /// <param name="ensureStarter">Load creates the starter file when the file is missing (the real file; tests leave it off).</param>
    public MessageEditorState(string path, Func<bool>? canWrite = null, bool ensureStarter = false)
    {
        _path = path;
        _canWrite = canWrite ?? (() => true);
        _ensureStarter = ensureStarter;
    }

    public MessageEditorMode Mode { get; private set; } = MessageEditorMode.List;
    public MessageDocument Document { get; set; } = new();
    /// <summary>The Text view's buffer.</summary>
    public string Text { get; set; } = "";
    /// <summary>The styled row whose controls show.</summary>
    public Guid? Expanded { get; set; }
    /// <summary>The row Add Line put in last, until the file is read again (the new-line anchor).</summary>
    public Guid? JustAdded { get; private set; }
    /// <summary>messages.txt changed under unsaved edits (Claude's lines were added): offer to reload.</summary>
    public bool FileChanged { get; set; }
    /// <summary>The file as last read or saved.</summary>
    public string SavedText { get; private set; } = "";
    public bool Loaded { get; private set; }

    /// <summary>What the current view would save.</summary>
    public string CurrentText => Mode == MessageEditorMode.List ? Document.Text : Text;

    public bool Unsaved => CurrentText != SavedText;

    /// <summary>
    /// Writes the starter file when there is no messages.txt yet, so there is something to edit. Does
    /// nothing once Reset has run (<paramref name="canWrite"/> false) or when the file exists.
    /// </summary>
    public static void EnsureFile(string path, Func<bool>? canWrite = null)
    {
        if (!(canWrite?.Invoke() ?? true) || File.Exists(path)) return;
        try
        {
            MessageFileIo.WriteAtomic(path, MessageFileIo.Encode(DeskMessages.Starter + "\n"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Best effort, as on the Mac.
        }
    }

    /// <summary>Reads the file (creating the starter the first time if asked to), dropping unsaved edits.</summary>
    public void Load()
    {
        if (_ensureStarter) EnsureFile(_path, _canWrite);
        string read = MessageFileIo.ReadText(_path) ?? "";
        SavedText = read;
        Text = read;
        Document = MessageDocument.Parse(read);
        Expanded = null;
        FileChanged = false;
        JustAdded = null;
        Loaded = true;
    }

    public void LoadIfNeeded()
    {
        if (!Loaded) Load();
    }

    /// <summary>Writes what the current view shows; the list's rows now read as saved. False when it could not be written.</summary>
    public bool Save()
    {
        if (!_canWrite()) return false;
        string output = CurrentText;
        try
        {
            MessageFileIo.WriteAtomic(_path, MessageFileIo.Encode(output));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        SavedText = output;
        if (Mode == MessageEditorMode.List)
        {
            Document.Rebase();
            Text = output;
        }
        else
        {
            Document = MessageDocument.Parse(output);
        }
        FileChanged = false;
        return true;
    }

    /// <summary>The Text view: the list's rows as text, unsaved edits included.</summary>
    public void ShowText()
    {
        if (Mode != MessageEditorMode.List) return;
        Text = Document.Text;
        Mode = MessageEditorMode.Text;
    }

    /// <summary>Back to the list: the text read into rows, unsaved edits included.</summary>
    public void ShowList()
    {
        if (Mode != MessageEditorMode.Text) return;
        Document = MessageDocument.Parse(Text);
        Expanded = null;
        Mode = MessageEditorMode.List;
    }

    /// <summary>A new row after the last one, its controls open.</summary>
    public void AddLine(MessageLine? line = null)
    {
        Expanded = Document.Add(line);
        JustAdded = Expanded;
    }

    /// <summary>
    /// Add Line: from either view, a new row at the top of its day's lines
    /// (<see cref="MessageDocument.InsertAtTopOfGroup"/>), its controls open; the page scrolls it into view.
    /// </summary>
    public void AddLineAtTop(MessageLine? line = null)
    {
        LoadIfNeeded();
        if (Mode == MessageEditorMode.Text) ShowList();
        Expanded = Document.InsertAtTopOfGroup(line);
        JustAdded = Expanded;
    }
}
