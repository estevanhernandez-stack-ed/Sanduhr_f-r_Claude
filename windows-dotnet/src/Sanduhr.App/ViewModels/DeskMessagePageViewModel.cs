using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// One entry of a combo box on the Message page: what the box shows and the value it stands for. Records
/// compare by value, so a box finds the item for the current value by equality. The box draws
/// <see cref="ToString"/>, so no template is needed.
/// </summary>
public sealed record MessageChoice<T>(string Label, T Value)
{
    public override string ToString() => Label;
}

/// <summary>The Motion box's entries: a motion and, for Sweep, how often it repeats (null is once).</summary>
public sealed record MessageMotionChoice(MessageMotionKind Kind, double? Period, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// One row of the Message page's list: a line of messages.txt drawn as controls (when, text, color,
/// glow, size, letters, motion) or, for a line the editor cannot represent, as the text kept as written.
/// The row holds no copy of the line: every get reads the page's document and every set edits it, so the
/// list and the Text view stay one document (spec 6.2).
/// </summary>
public sealed partial class MessageRowViewModel : ObservableObject
{
    private static readonly IReadOnlyList<MessageChoice<string>> WhenChoices =
    [
        new("Every day", "every"),
        .. MessageWhen.Weekdays.Select(d => new MessageChoice<string>(MessageWhen.WeekdayNames[d] + "s", d)),
        new("A date", "date"),
    ];

    private static readonly IReadOnlyList<MessageChoice<int>> Months =
        [.. MessageWhen.MonthNames.Select((n, i) => new MessageChoice<int>(n, i + 1))];

    private static readonly IReadOnlyList<MessageChoice<bool?>> GlowChoicesList =
        [new("As the Desk", null), new("On", true), new("Off", false)];

    private static readonly IReadOnlyList<MessageChoice<LetterStyle?>> LetterChoicesList =
        [new("As written", null), .. MessageLetters.Order.Select(s => new MessageChoice<LetterStyle?>(LetterStyles.Title(s), s))];

    private static readonly IReadOnlyList<MessageChoice<double?>> StandardSizes =
        [new("Default", null), .. Enumerable.Range(5, 16).Where(t => t != 10)
            .Select(t => new MessageChoice<double?>($"{t / 10.0:0.0}×", t / 10.0))];

    private static readonly double[] SweepPeriods = [10, 20, 30, 60];

    private readonly DeskMessagePageViewModel _owner;
    private bool _customColor;

    internal MessageRowViewModel(DeskMessagePageViewModel owner, MessageRow row, bool focusMe, int number = 1)
    {
        _owner = owner;
        Id = row.Id;
        Number = number;
        IsNew = focusMe;
        _customColor = row.Line is { } line && ColorShown(line.Look) == CustomColorChoice;
        SizeChoices = SizesFor(row.Line);
        MotionChoices = MotionsFor(row.Line);
    }

    public Guid Id { get; }

    /// <summary>The line's place in the list, from 1. The row's controls are named with it ("Remove line 2"), so a
    /// screen reader can tell one line's buttons from the next; the list is rebuilt on every move.</summary>
    public int Number { get; }

    /// <summary>True for the row Add line just put in, until the page has moved focus to its text box.</summary>
    public bool IsNew { get; set; }

    private MessageRow? Row => _owner.RowOf(Id);
    private MessageLine Line => Row?.Line ?? new MessageLine();

    /// <summary>A line the controls can set; false is a raw row, shown as text kept as written.</summary>
    public bool IsStyled => Row?.Content.Kind == MessageContentKind.Line;
    public bool IsRaw => !IsStyled;

    // The text

    public string Text
    {
        get => Line.Text;
        set
        {
            if (Line.Text == value) return;
            _owner.Edit(Id, l => l with { Text = value });
        }
    }

    /// <summary>A raw row's text, as it is in the file.</summary>
    public string RawText
    {
        get => Row?.Content.Text ?? "";
        set
        {
            if (RawText == value) return;
            _owner.EditRaw(Id, value);
        }
    }

    // When

    public IReadOnlyList<MessageChoice<string>> WhenOptions => WhenChoices;
    public IReadOnlyList<MessageChoice<int>> MonthOptions => Months;

    public MessageChoice<string> When
    {
        get
        {
            var w = Line.When;
            string key = w.Kind switch { MessageWhenKind.EveryDay => "every", MessageWhenKind.Weekday => w.Day ?? "every", _ => "date" };
            return WhenChoices.FirstOrDefault(c => c.Value == key) ?? WhenChoices[0];
        }
        set
        {
            if (value is null || value == When) return;
            _owner.Edit(Id, l => l with
            {
                When = value.Value switch
                {
                    "every" => MessageWhen.EveryDay,
                    "date" => l.When.Kind == MessageWhenKind.Date ? l.When : MessageWhen.OnDate(_owner.Today.Month, _owner.Today.Day),
                    _ => MessageWhen.OnWeekday(value.Value),
                },
            });
            OnPropertyChanged(nameof(When));
            OnPropertyChanged(nameof(IsDate));
            OnPropertyChanged(nameof(Month));
            OnPropertyChanged(nameof(DayOptions));
            OnPropertyChanged(nameof(Day));
        }
    }

    public bool IsDate => Line.When.Kind == MessageWhenKind.Date;

    public MessageChoice<int> Month
    {
        get => Months[Math.Clamp((IsDate ? Line.When.Month : 1) - 1, 0, 11)];
        set
        {
            if (value is null || !IsDate) return;
            int day = Math.Min(Line.When.DayOfMonth, MessageWhen.DaysInMonth(value.Value));
            _owner.Edit(Id, l => l with { When = MessageWhen.OnDate(value.Value, day) });
            OnPropertyChanged(nameof(DayOptions));
            OnPropertyChanged(nameof(Day));
        }
    }

    public IReadOnlyList<int> DayOptions => DayLists[(IsDate ? Line.When.Month : 1) - 1];

    private static readonly IReadOnlyList<int>[] DayLists =
        [.. Enumerable.Range(1, 12).Select(m => (IReadOnlyList<int>)[.. Enumerable.Range(1, MessageWhen.DaysInMonth(m))])];

    public int Day
    {
        get => IsDate ? Line.When.DayOfMonth : 1;
        set
        {
            if (!IsDate || value < 1) return;
            _owner.Edit(Id, l => l with { When = MessageWhen.OnDate(l.When.Month, value) });
        }
    }

    // Color

    private const string MessageColorChoice = "Message color";
    private const string CustomColorChoice = "Custom";

    /// <summary>"Message color" (the Desk's), a named palette, or "Custom".</summary>
    public IReadOnlyList<string> ColorOptions { get; } =
        [MessageColorChoice, .. MessagePalette.All.Select(p => p.Name), CustomColorChoice];

    private static string ColorShown(MessageLook look) =>
        look.Ink is not { Count: > 0 } ? MessageColorChoice : MessagePalette.NameOf(look.Ink) ?? CustomColorChoice;

    public string Color
    {
        get => _customColor && Line.Look.Ink is { Count: > 0 } ? CustomColorChoice : ColorShown(Line.Look);
        set
        {
            if (value is null || value == Color) return;
            _customColor = value == CustomColorChoice;
            _customInk = null;
            _owner.Edit(Id, l => l with
            {
                Look = value switch
                {
                    MessageColorChoice => l.Look with { Ink = null },
                    CustomColorChoice => l.Look.Ink is { Count: > 0 } ? l.Look : l.Look.SetInkMode(MessageInkMode.Solid),
                    _ => l.Look with { Ink = MessagePalette.Colors(value) },
                },
            });
            OnPropertyChanged(nameof(Color));
            OnPropertyChanged(nameof(IsCustomColor));
            OnPropertyChanged(nameof(CustomInk));
            OnPropertyChanged(nameof(CustomInkProblem));
        }
    }

    public bool IsCustomColor => Color == CustomColorChoice;

    private string? _customInk;

    /// <summary>The custom color as typed: one to four hex colors, comma separated ("ff2a6d,05d9e8").</summary>
    public string CustomInk
    {
        get => _customInk ?? (Line.Look.Ink is { Count: > 0 } now ? string.Join(",", now) : "");
        set
        {
            _customInk = value;
            if (Parse(value) is { } colors && !(Line.Look.Ink?.SequenceEqual(colors) ?? false))
                _owner.Edit(Id, l => l with { Look = l.Look with { Ink = colors } });
            OnPropertyChanged(nameof(CustomInkProblem));
        }
    }

    /// <summary>Why the custom colors are not applied yet, or null.</summary>
    public string? CustomInkProblem =>
        IsCustomColor && Parse(CustomInk) is null ? "Use one to four hex colors, like ff2a6d,05d9e8." : null;

    private static IReadOnlyList<string>? Parse(string text)
    {
        var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > MessageLook.MaxInk) return null;
        var hexes = parts.Select(InkHex.Normalize).ToList();
        return hexes.Any(h => h is null) ? null : hexes.Select(h => h!).ToArray();
    }

    // Glow, size, letters, motion

    public IReadOnlyList<MessageChoice<bool?>> GlowOptions => GlowChoicesList;

    public MessageChoice<bool?> Glow
    {
        get => GlowChoicesList.First(c => c.Value == Line.Look.Glow);
        set
        {
            if (value is null) return;
            _owner.Edit(Id, l => l with { Look = l.Look with { Glow = value.Value } });
        }
    }

    public IReadOnlyList<MessageChoice<double?>> SizeChoices { get; }

    private static IReadOnlyList<MessageChoice<double?>> SizesFor(MessageLine? line)
    {
        if (line?.Look.Size is not { } size || StandardSizes.Any(c => c.Value is { } v && Math.Abs(v - size) < 0.001))
            return StandardSizes;
        return [.. StandardSizes.Append(new MessageChoice<double?>(MessageLook.Number(size) + "×", size))
            .OrderBy(c => c.Value ?? 1)];
    }

    public MessageChoice<double?> Size
    {
        get
        {
            double? s = Line.Look.Size;
            return SizeChoices.FirstOrDefault(c => c.Value is null ? s is null : s is { } v && Math.Abs(c.Value.Value - v) < 0.001)
                ?? SizeChoices[0];
        }
        set
        {
            if (value is null) return;
            _owner.Edit(Id, l => l with { Look = l.Look with { Size = value.Value } });
        }
    }

    public IReadOnlyList<MessageChoice<LetterStyle?>> LetterOptions => LetterChoicesList;

    public MessageChoice<LetterStyle?> Letters
    {
        get => LetterChoicesList.First(c => c.Value == Line.Look.Font);
        set
        {
            if (value is null) return;
            _owner.Edit(Id, l => l with { Look = l.Look with { Font = value.Value } });
        }
    }

    public IReadOnlyList<MessageMotionChoice> MotionChoices { get; }

    private static IReadOnlyList<MessageMotionChoice> MotionsFor(MessageLine? line)
    {
        var list = new List<MessageMotionChoice>
        {
            new(MessageMotionKind.None, null, MessageMotionKinds.Title(MessageMotionKind.None)),
            new(MessageMotionKind.Write, null, MessageMotionKinds.Title(MessageMotionKind.Write)),
            new(MessageMotionKind.Shimmer, null, MessageMotionKinds.Title(MessageMotionKind.Shimmer)),
            new(MessageMotionKind.Sweep, null, "Sweep once"),
        };
        var periods = SweepPeriods.AsEnumerable();
        if (line?.Look is { Motion: MessageMotionKind.Sweep, SweepPeriod: { } own } && !SweepPeriods.Contains(own))
            periods = periods.Append(own).OrderBy(p => p);
        list.AddRange(periods.Select(p => new MessageMotionChoice(MessageMotionKind.Sweep, p, $"Sweep every {MessageLook.Number(p)} s")));
        return list;
    }

    public MessageMotionChoice Motion
    {
        get
        {
            var look = Line.Look;
            return MotionChoices.FirstOrDefault(c => c.Kind == look.Motion && (look.Motion != MessageMotionKind.Sweep || c.Period == look.SweepPeriod))
                ?? MotionChoices[0];
        }
        set
        {
            if (value is null) return;
            _owner.Edit(Id, l => l with
            {
                Look = l.Look.SetMotion(value.Kind) with { SweepPeriod = value.Kind == MessageMotionKind.Sweep ? value.Period : null },
            });
        }
    }

    // What the row does and what is wrong with it

    /// <summary>When the line shows and what it does to the day's other lines.</summary>
    public string Note => Row is { } row ? _owner.NoteOf(row) : "";

    /// <summary>Why the Desk would read the written line differently, or null.</summary>
    public string? Problem => Line is { } l && IsStyled ? l.Problem : null;

    /// <summary>The line text for the screen reader.</summary>
    public string Summary => IsStyled ? Line.CleanText : RawText;

    /// <summary>What a screen reader reads for the row: the line itself, never the type name.</summary>
    public override string ToString() => string.IsNullOrWhiteSpace(Summary) ? "Empty message line" : Summary;

    internal void RefreshDerived()
    {
        OnPropertyChanged(nameof(Note));
        OnPropertyChanged(nameof(Problem));
        OnPropertyChanged(nameof(Summary));
    }

    internal void RefreshAll() => OnPropertyChanged(string.Empty);

    [RelayCommand] private void MoveUp() => _owner.Move(Id, -1);
    [RelayCommand] private void MoveDown() => _owner.Move(Id, 1);
    [RelayCommand] private void Remove() => _owner.Remove(Id);
}

/// <summary>
/// Settings, Message (spec 6.2, ported from the Mac's MessageEditorView.swift): the line editor as rows
/// or as text, Save and Revert, the rotation, special days, the message's look, and Claude's suggestion
/// card (spec 6.9). The file half is Core's <see cref="MessageEditorState"/>; this class keeps the rows
/// in step with it and writes the settings. Settings changes (rotation, special days, look) save at
/// once; the file saves with Save. <c>changed</c> runs after every save of the file or the settings so
/// the running Desk redraws. Message text is never logged.
/// </summary>
public sealed partial class DeskMessagePageViewModel : ObservableObject, IDisposable
{
    /// <summary>Slider drags settle for this long before they save, as on the Desk page.</summary>
    public static readonly TimeSpan SettleFor = TimeSpan.FromMilliseconds(200);

    /// <summary>The Color box's last entry: a hex the user types.</summary>
    public const string CustomColor = "Custom";

    private static readonly MessageChoice<string> FollowsInk = new("Follows the Desk ink", "");
    private static readonly MessageChoice<string> CustomInkChoice = new(CustomColor, "\u0001custom");

    private readonly SettingsStore _store;
    private readonly string _path;
    private readonly Func<bool> _canWrite;
    private readonly Action _changed;
    private readonly MessageEditorState _editor;
    private readonly DeskMessageHandoff? _handoff;
    private readonly DispatcherTimer _settle;
    private DeskMessageSettings _settings;
    private bool _settingsPending;
    private bool _customColorPicked;
    private Guid? _focusNext;
    private bool _active;

    /// <param name="store">Reads and writes the message settings (<c>desk_message</c>).</param>
    /// <param name="messagesPath">messages.txt.</param>
    /// <param name="canWrite">False once Reset has run: nothing is written then.</param>
    /// <param name="changed">Called after every save of the file or the settings, so the Desk redraws.</param>
    /// <param name="fontFamilies">The families the message font box offers, after "Same as the Desk".</param>
    /// <param name="handoff">Claude's suggestions, or null where there is no handoff.</param>
    public DeskMessagePageViewModel(SettingsStore store, string messagesPath, Func<bool> canWrite, Action changed,
        IReadOnlyList<string> fontFamilies, DeskMessageHandoff? handoff = null)
    {
        _store = store;
        _path = messagesPath;
        _canWrite = canWrite;
        _changed = changed;
        _handoff = handoff;
        _editor = new MessageEditorState(messagesPath, canWrite, ensureStarter: true);
        _settings = store.LoadDeskMessage();
        _fontFamilies = fontFamilies;
        _settle = new DispatcherTimer(DispatcherPriority.Background) { Interval = SettleFor };
        _settle.Tick += (_, _) => Flush();
        if (_handoff is not null)
        {
            _handoff.SuggestionReceived += OnSuggestionEvent;
            _handoff.Decided += OnDecided;
            _handoff.Applied += OnApplied;
        }
        FontOptions = BuildFontOptions();
        ColorOptions = [FollowsInk, .. DeskInkPresets.All.Select(p => new MessageChoice<string>(p.Name, p.Value)), CustomInkChoice];
        SecondsOptions = [.. MessageSpecialModes.Presets.Select(s => new MessageChoice<double>(MessageSpecialModes.Label(s), s))];
    }

    private readonly IReadOnlyList<string> _fontFamilies;

    /// <summary>Today, for a new date row's starting month and day.</summary>
    internal DateTime Today => DateTime.Today;

    /// <summary>The rows of the list, in file order, comments and blank lines left out.</summary>
    public ObservableCollection<MessageRowViewModel> Rows { get; } = [];

    // Loading

    /// <summary>
    /// The host calls this each time the page is shown (the control also calls it itself when it becomes
    /// visible). The first call reads messages.txt, creating the starter file when there is none; later
    /// calls pick up a change made outside Sanduhr: silently when nothing is unsaved, otherwise as the
    /// reload offer. The message settings are read again too.
    /// </summary>
    public void Activate()
    {
        _active = true;
        _settings = _store.LoadDeskMessage();
        _customColorText = null;
        RaiseSettings();
        if (!_editor.Loaded)
        {
            _editor.Load();
            Rebuild();
        }
        else
        {
            CheckDisk();
        }
        RaiseSuggestion();
    }

    /// <summary>
    /// Desk Look shows the message's size, color, glow and font without the rest of the page: it reads the saved settings
    /// again (a design switch or a proposal may have changed them) so its controls are fresh and an edit keeps the other fields.
    /// </summary>
    public void RefreshSettings()
    {
        _settings = _store.LoadDeskMessage();
        _customColorText = null;
        RaiseSettings();
    }

    private void CheckDisk()
    {
        if (!_editor.Loaded) return;
        string disk = ReadDisk() ?? "";
        if (disk == _editor.SavedText) return;
        if (_editor.Unsaved)
        {
            _editor.FileChanged = true;
            RaiseState();
        }
        else
        {
            _editor.Load();
            Rebuild();
        }
    }

    private string? ReadDisk()
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(File.ReadAllBytes(_path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return null;
        }
    }

    // The document and its rows

    internal MessageRow? RowOf(Guid id) => _editor.Document.Rows.FirstOrDefault(r => r.Id == id);

    internal string NoteOf(MessageRow row) => _editor.Document.Note(row, _settings.Mix);

    private void Rebuild()
    {
        Rows.Clear();
        int number = 0;
        foreach (var row in _editor.Document.MessageRows)
        {
            bool focus = _focusNext == row.Id;
            Rows.Add(new MessageRowViewModel(this, row, focus, ++number));
        }
        _focusNext = null;
        RaiseState();
    }

    /// <summary>A control changed a styled row's line.</summary>
    internal void Edit(Guid id, Func<MessageLine, MessageLine> change)
    {
        _editor.Document.Update(id, change);
        AfterEdit(id);
    }

    internal void EditRaw(Guid id, string text)
    {
        var rows = _editor.Document.Rows;
        int i = rows.FindIndex(r => r.Id == id);
        if (i < 0 || rows[i].Content.Kind != MessageContentKind.Raw) return;
        rows[i] = rows[i] with { Content = MessageContent.Raw(text) };
        AfterEdit(id);
    }

    private void AfterEdit(Guid id)
    {
        foreach (var row in Rows)
        {
            if (row.Id == id) row.RefreshAll();
            else row.RefreshDerived();
        }
        RaiseState();
    }

    internal void Move(Guid id, int step)
    {
        _editor.Document.MoveBy(id, step);
        Rebuild();
    }

    internal void Remove(Guid id)
    {
        _editor.Document.Delete(id);
        Rebuild();
    }

    // The bar: Add line, view switch, Save, Revert

    /// <summary>The Message piece's line at the page top (set by <see cref="SettingsViewModel"/>).</summary>
    public DeskPieceStatusViewModel? Piece { get; internal set; }

    [RelayCommand]
    private void AddLine()
    {
        _editor.AddLineAtTop();
        _focusNext = _editor.JustAdded;
        Rebuild();
        OnPropertyChanged(nameof(EditorText));
        // Writing a line is turning the message on: a hidden piece is shown (Principle 4).
        Piece?.PlaceIfHidden();
    }

    [RelayCommand]
    private void ToggleMode()
    {
        if (_editor.Mode == MessageEditorMode.List) _editor.ShowText();
        else _editor.ShowList();
        Rebuild();
        OnPropertyChanged(nameof(EditorText));
    }

    [RelayCommand]
    private void Save()
    {
        if (_editor.Save())
        {
            SaveMessage = "";
            Rebuild();
            OnPropertyChanged(nameof(EditorText));
            _changed();
        }
        else
        {
            SaveMessage = "messages.txt could not be saved.";
            RaiseState();
        }
    }

    [RelayCommand]
    private void Revert()
    {
        SaveMessage = "";
        _editor.Load();
        Rebuild();
        OnPropertyChanged(nameof(EditorText));
    }

    [RelayCommand]
    private void Reload() => Revert();

    [ObservableProperty] private string _saveMessage = "";

    /// <summary>The List radio. Checking it shows the list; the false WPF writes when Text takes the check does nothing.</summary>
    public bool IsListMode
    {
        get => _editor.Mode == MessageEditorMode.List;
        set { if (value && !IsListMode) ToggleMode(); }
    }

    /// <summary>The Text radio, the other half of the segment.</summary>
    public bool IsTextMode
    {
        get => !IsListMode;
        set { if (value && IsListMode) ToggleMode(); }
    }
    public bool HasNoRows => Rows.Count == 0;

    /// <summary>Unsaved edits: the text this view would write differs from the file as read.</summary>
    public bool Unsaved => _editor.Unsaved;

    /// <summary>messages.txt changed on disk under unsaved edits: the reload offer.</summary>
    public bool FileChanged => _editor.FileChanged;

    public string StatusText => SaveMessage.Length > 0 ? SaveMessage : Unsaved ? "Unsaved changes" : "";

    public string FileChangedText => "messages.txt changed outside Sanduhr. Reload?";

    /// <summary>The Text view's buffer.</summary>
    public string EditorText
    {
        get => _editor.Text;
        set
        {
            if (_editor.Text == value) return;
            _editor.Text = value;
            RaiseState();
        }
    }

    partial void OnSaveMessageChanged(string value) => OnPropertyChanged(nameof(StatusText));

    private void RaiseState()
    {
        OnPropertyChanged(nameof(IsListMode));
        OnPropertyChanged(nameof(IsTextMode));
        OnPropertyChanged(nameof(HasNoRows));
        OnPropertyChanged(nameof(Unsaved));
        OnPropertyChanged(nameof(FileChanged));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CanAddSuggestion));
        OnPropertyChanged(nameof(SuggestionAddTip));
    }

    // Settings: rotation, special days, look

    public bool IsDaily
    {
        get => !_settings.Hourly;
        set { if (value) Apply(_settings with { Hourly = false }); }
    }

    public bool IsHourly
    {
        get => _settings.Hourly;
        set { if (value) Apply(_settings with { Hourly = true }); }
    }

    public bool Mix
    {
        get => _settings.Mix;
        set => Apply(_settings with { Mix = value });
    }

    /// <summary>The pinned line; empty rotates. It saves when the box loses focus.</summary>
    public string Pin
    {
        get => _settings.Pin;
        set => Apply(_settings with { Pin = value ?? "" });
    }

    [RelayCommand]
    private void ClearPin() => Pin = "";

    public bool IsStack
    {
        get => _settings.Special == MessageSpecialMode.Stack;
        set { if (value) Apply(_settings with { Special = MessageSpecialMode.Stack }); }
    }

    public bool IsTurns
    {
        get => _settings.Special == MessageSpecialMode.Turns;
        set { if (value) Apply(_settings with { Special = MessageSpecialMode.Turns }); }
    }

    public bool IsScroll
    {
        get => _settings.Special == MessageSpecialMode.Scroll;
        set { if (value) Apply(_settings with { Special = MessageSpecialMode.Scroll }); }
    }

    public string StackHelp => MessageSpecialModes.Help(MessageSpecialMode.Stack);
    public string TurnsHelp => MessageSpecialModes.Help(MessageSpecialMode.Turns);
    public string ScrollHelp => MessageSpecialModes.Help(MessageSpecialMode.Scroll);

    /// <summary>How long each line shows applies to Take turns and Scroll; Stack shows them all at once.</summary>
    public bool SecondsApply => _settings.Special != MessageSpecialMode.Stack;

    public IReadOnlyList<MessageChoice<double>> SecondsOptions { get; }

    public MessageChoice<double> Seconds
    {
        get => SecondsOptions.FirstOrDefault(c => c.Value == _settings.Seconds) ?? SecondsOptions[1];
        set { if (value is not null) Apply(_settings with { Seconds = value.Value }); }
    }

    public double MinSize => DeskMessageSettings.MinSize;
    public double MaxSize => DeskMessageSettings.MaxSize;

    /// <summary>The message size, 24 to 180. A drag settles briefly before it saves.</summary>
    public double Size
    {
        get => _settings.Size;
        set
        {
            double v = Math.Clamp(Math.Round(value), DeskMessageSettings.MinSize, DeskMessageSettings.MaxSize);
            if (v == _settings.Size) return;
            _settings = _settings with { Size = v };
            _settingsPending = true;
            _settle.Stop();
            _settle.Start();
            OnPropertyChanged(nameof(Size));
        }
    }

    public IReadOnlyList<MessageChoice<string>> ColorOptions { get; }

    private bool IsPresetColor(string value) => DeskInkPresets.All.Any(p => p.Value == value);

    public MessageChoice<string> Color
    {
        get
        {
            string c = _settings.Color;
            if (c.Length == 0) return FollowsInk;
            if (_customColorPicked || !IsPresetColor(c)) return CustomInkChoice;
            return ColorOptions.First(o => o.Value == c);
        }
        set
        {
            if (value is null) return;
            if (value == CustomInkChoice)
            {
                _customColorPicked = true;
                Apply(_settings.Color.Length == 0 ? _settings with { Color = "9ad7ff" } : _settings);
                RaiseColor();
                return;
            }
            _customColorPicked = false;
            _customColorText = null;
            Apply(_settings with { Color = value.Value });
            RaiseColor();
        }
    }

    public bool IsCustomColor => Color == CustomInkChoice;

    private string? _customColorText;
    private bool _typingColor;

    /// <summary>The custom color as typed: hex colors, comma separated. It saves once every entry is a hex color.</summary>
    public string CustomColorText
    {
        get => _customColorText ?? _settings.Color;
        set
        {
            _customColorText = value;
            _typingColor = true;
            try { ApplyCustomColor(value ?? ""); }
            finally { _typingColor = false; }
            OnPropertyChanged(nameof(CustomColorProblem));
        }
    }

    private void ApplyCustomColor(string value)
    {
        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var hexes = parts.Select(InkHex.Normalize).ToList();
        if (parts.Length > 0 && hexes.All(h => h is not null))
            Apply(_settings with { Color = string.Join(",", hexes!) });
    }

    public string? CustomColorProblem =>
        IsCustomColor && (_customColorText ?? "").Trim().Length > 0
        && (_customColorText!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) is var p
            && (p.Length == 0 || p.Any(x => InkHex.Normalize(x) is null)))
            ? "Use hex colors like 9ad7ff, or a few with commas for a gradient."
            : null;

    public bool Glow
    {
        get => _settings.Glow;
        set => Apply(_settings with { Glow = value });
    }

    public IReadOnlyList<MessageChoice<string>> FontOptions { get; }

    private IReadOnlyList<MessageChoice<string>> BuildFontOptions()
    {
        var list = new List<MessageChoice<string>> { new("Same as the Desk", "") };
        list.AddRange(_fontFamilies.Select(f => new MessageChoice<string>(f, f)));
        if (_settings.Font.Length > 0 && list.All(c => c.Value != _settings.Font))
            list.Add(new MessageChoice<string>(_settings.Font, _settings.Font));
        return list;
    }

    public MessageChoice<string> Font
    {
        get => FontOptions.FirstOrDefault(c => c.Value == _settings.Font) ?? FontOptions[0];
        set { if (value is not null) Apply(_settings with { Font = value.Value }); }
    }

    private void Apply(DeskMessageSettings next)
    {
        if (next == _settings && !_settingsPending) return;
        _settings = next;
        _settle.Stop();
        _settingsPending = true;
        Flush();
        RaiseSettings();
    }

    /// <summary>Writes a pending settings change now (a slider drag settles to this) and tells the Desk.</summary>
    public void Flush()
    {
        _settle.Stop();
        if (!_settingsPending) return;
        _settingsPending = false;
        if (!_canWrite()) return;
        _store.SaveDeskMessage(_settings);
        _changed();
    }

    private void RaiseSettings()
    {
        foreach (var name in new[]
        {
            nameof(IsDaily), nameof(IsHourly), nameof(Mix), nameof(Pin), nameof(IsStack), nameof(IsTurns), nameof(IsScroll),
            nameof(SecondsApply), nameof(Seconds), nameof(Size), nameof(Glow), nameof(Font),
        })
            OnPropertyChanged(name);
        RaiseColor();
        foreach (var row in Rows) row.RefreshDerived();
    }

    private void RaiseColor()
    {
        OnPropertyChanged(nameof(Color));
        OnPropertyChanged(nameof(IsCustomColor));
        if (!_typingColor) OnPropertyChanged(nameof(CustomColorText));
        OnPropertyChanged(nameof(CustomColorProblem));
    }

    // Claude's suggestion (spec 6.9)

    private MessageProposal? Pending => _handoff?.Pending;

    public bool HasSuggestion => Pending is not null;

    public string SuggestionTitle => Pending is { } p
        ? MessageSuggestionCardText.Headline(p)
        : "";

    public string SuggestionNote => Pending?.Note ?? "";

    public string SuggestionReplaceNote => Pending is { Mode: MessageProposalMode.Replace }
        ? "Replace keeps the notes at the top of messages.txt and swaps every other line for these. Your list now is kept as messages.txt.previous."
        : "";

    public string SuggestionAddText => Pending is { Mode: MessageProposalMode.Replace } ? "Replace" : "Add";

    /// <summary>Add would write under unsaved edits, so it waits for Save or Revert.</summary>
    public bool CanAddSuggestion => !_editor.Unsaved;

    public string SuggestionAddTip => CanAddSuggestion ? "" : "Save or reload your edits first.";

    /// <summary>The suggested lines (message lines and the notes with them), shown read-only by Review.</summary>
    public IReadOnlyList<string> SuggestionLines => Pending is { } p
        ? [.. p.Lines.Where(l => !string.IsNullOrWhiteSpace(l))]
        : [];

    [ObservableProperty] private bool _reviewOpen;

    public string ReviewButtonText => ReviewOpen ? "Hide" : "Review";

    partial void OnReviewOpenChanged(bool value) => OnPropertyChanged(nameof(ReviewButtonText));

    [RelayCommand]
    private void AddSuggestion()
    {
        if (!CanAddSuggestion) return;
        ReviewOpen = false;
        _handoff?.Approve();
    }

    [RelayCommand]
    private void ReviewSuggestion() => ReviewOpen = !ReviewOpen;

    [RelayCommand]
    private void DismissSuggestion()
    {
        ReviewOpen = false;
        _handoff?.Dismiss();
    }

    private void RaiseSuggestion()
    {
        OnPropertyChanged(nameof(HasSuggestion));
        OnPropertyChanged(nameof(SuggestionTitle));
        OnPropertyChanged(nameof(SuggestionNote));
        OnPropertyChanged(nameof(SuggestionReplaceNote));
        OnPropertyChanged(nameof(SuggestionAddText));
        OnPropertyChanged(nameof(SuggestionLines));
        OnPropertyChanged(nameof(CanAddSuggestion));
        OnPropertyChanged(nameof(SuggestionAddTip));
    }

    private void OnSuggestionEvent(MessageProposal _) => OnUi(RaiseSuggestion);

    private void OnDecided(MessageProposal _) => OnUi(() =>
    {
        ReviewOpen = false;
        RaiseSuggestion();
    });

    /// <summary>messages.txt was changed here (Claude's lines went in): reload, or offer to under unsaved edits.</summary>
    private void OnApplied() => OnUi(() =>
    {
        if (_active) CheckDisk();
        RaiseSuggestion();
    });

    private static void OnUi(Action action)
    {
        var d = Application.Current?.Dispatcher;
        if (d is null || d.CheckAccess()) action();
        else d.BeginInvoke(action);
    }

    public void Dispose()
    {
        Flush();
        if (_handoff is not null)
        {
            _handoff.SuggestionReceived -= OnSuggestionEvent;
            _handoff.Decided -= OnDecided;
            _handoff.Applied -= OnApplied;
        }
    }
}

/// <summary>The suggestion card's headline, as the Mac words it (MessageSuggestionReview.headline).</summary>
internal static class MessageSuggestionCardText
{
    /// <summary>"Claude suggested 3 lines", or "... to replace your list".</summary>
    public static string Headline(MessageProposal p)
    {
        int n = p.MessageLines.Count;
        string lines = $"{n} line{(n == 1 ? "" : "s")}";
        return p.Mode == MessageProposalMode.Replace ? $"Claude suggested {lines} to replace your list" : $"Claude suggested {lines}";
    }
}
