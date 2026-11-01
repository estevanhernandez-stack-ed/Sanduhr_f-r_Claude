using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sanduhr.App.Services;
using Sanduhr.Core;

namespace Sanduhr.App.ViewModels;

/// <summary>
/// Design it with an AI chat (setup spec, section 3): copy a prompt, paste the answer back, see each problem by field, then Preview
/// the Desk part through the review bar or use the drawer rules, which replace the rules and open their plan for review. The parts are
/// checked by their own rules (<see cref="DeskDesign.Validate(JsonNode?, DeskDesignContext)"/> for the Desk, <see cref="PastedSetup"/> for
/// the rules and the drawer look), and nothing applies while anything in the paste is wrong. Copy here names no product (<see cref="SetupCopy.Chat"/>).
/// </summary>
public sealed partial class ChatDesignViewModel : ObservableObject
{
    private readonly Func<IReadOnlyList<string>> _monitorNames;
    private readonly Func<DeskValidation, DeskAnswer>? _previewDesk;
    private readonly Func<JsonNode, DeskValidation> _validate;
    private readonly Func<IReadOnlyList<DrawerRule>, IReadOnlyList<string>, string?> _useRules;
    private readonly Func<PastedDrawerLook, string?>? _useDrawerSettings;
    private DeskValidation? _desk;
    private PastedDrawerLook? _drawerLook;
    private IReadOnlyList<DrawerRule>? _rules;
    private IReadOnlyList<string> _ruleNames = [];

    /// <param name="monitorNames">The monitors by name, for the prompt (never an id).</param>
    /// <param name="previewDesk">Shows a checked Desk part through the review bar; null where there is no Desk to show it on.</param>
    /// <param name="validate">The Desk's rules for the Desk part, with this PC's context.</param>
    /// <param name="useRules">Replaces the drawer rules with the chat's and opens their plan for review: the rules and the chat's name for
    /// each rule's drawer. Null when the review opened; otherwise the line to show.</param>
    /// <param name="useDrawerSettings">Saves the chat's drawer look and opacity and, with an arrangement, lines the drawers up with Undo on
    /// the drawers bar. Null when it was done; otherwise the line to show (nothing applied). Null where there is no Desk to apply it on.</param>
    internal ChatDesignViewModel(Func<IReadOnlyList<string>> monitorNames, Func<DeskValidation, DeskAnswer>? previewDesk,
        Func<JsonNode, DeskValidation> validate, Func<IReadOnlyList<DrawerRule>, IReadOnlyList<string>, string?> useRules,
        Func<PastedDrawerLook, string?>? useDrawerSettings = null)
    {
        _monitorNames = monitorNames;
        _previewDesk = previewDesk;
        _validate = validate;
        _useRules = useRules;
        _useDrawerSettings = useDrawerSettings;
    }

    /// <summary>The prompt Copy prompt puts on the clipboard.</summary>
    public string PromptText => SetupPrompt.Build(_monitorNames());

    /// <summary>The paste's problems and advice, by field.</summary>
    public ObservableCollection<ThemeFindingViewModel> Findings { get; } = [];

    [ObservableProperty] private string _pasteText = "";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _rulesLine = "";
    [ObservableProperty] private string _drawerSettingsLine = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _canPreview;
    [ObservableProperty] private bool _canUseRules;
    [ObservableProperty] private bool _canUseDrawerSettings;

    /// <summary>Called when the window should close (the Close button).</summary>
    public Action? CloseRequested { get; set; }

    partial void OnPasteTextChanged(string value) => Check(value);

    private void Check(string raw)
    {
        Findings.Clear();
        _desk = null;
        _rules = null;
        _ruleNames = [];
        _drawerLook = null;
        CanPreview = CanUseRules = CanUseDrawerSettings = false;
        RulesLine = DrawerSettingsLine = Summary = Status = "";
        if (string.IsNullOrWhiteSpace(raw)) return;

        var read = PastedSetup.Read(raw);
        var errors = new List<DeskReason>(read.Reasons);
        var warnings = new List<DeskReason>();
        DeskValidation? verdict = null;
        // The Desk part is checked whatever the drawers part held, so every problem in the paste shows at once.
        if (read.Desk is { } deskNode)
        {
            verdict = _validate(deskNode);
            errors.AddRange(verdict.Reasons);
            warnings.AddRange(verdict.Warnings);
        }
        foreach (var f in PastedSetup.Findings(errors, warnings))
            Findings.Add(new ThemeFindingViewModel(f.Field, f.Text, f.IsError));

        if (errors.Count > 0)
        {
            Summary = SetupCopy.ChatFixCount(errors.Count);
            return;
        }

        // Nothing applies while anything in the paste is wrong, so a part is usable only when the whole paste is clean.
        if (verdict?.Design is not null) { _desk = verdict; CanPreview = _previewDesk is not null; }
        if (read.Rules is { Count: > 0 } rules)
        {
            _rules = rules;
            _ruleNames = read.RuleDrawerNames;
            CanUseRules = true;
            // The names are labels the chat chose; they show as a drawer's name does elsewhere, a codename while streamer mode is on.
            var shown = read.RuleDrawerNames.Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(n => StreamerMode.Mask.Name(CodenameCategory.Folder, n)).ToList();   // streamer: masked - Mask.Name gives each drawer name its codename while on
            RulesLine = SetupCopy.ChatRulesLine(rules.Count, shown);
        }
        if (read.DrawerLook is { } look)
        {
            _drawerLook = look;
            CanUseDrawerSettings = true;
            DrawerSettingsLine = DescribeDrawerSettings(look);
        }
        Summary = read.Blocks > 1 ? SetupCopy.ChatManyBlocks(read.Blocks) : SetupCopy.ChatReady;
    }

    /// <summary>What the drawer settings would do, in the window's words: the look and opacity, and where Tidy would line the drawers up,
    /// naming only the parts the paste gave.</summary>
    internal static string DescribeDrawerSettings(PastedDrawerLook look)
    {
        string? corner = look.Arrange is { } a && look.HasCorner ? CornerName(a.Corner) : null;
        string? flow = look.Arrange is { } b && look.HasFlow ? FlowName(b.Flow) : null;
        return SetupCopy.ChatDrawerSettingsLine(look.Look is { } k ? DrawerStyle.Word(k) : null, look.Opacity, corner, flow);
    }

    private static string CornerName(DrawerCorner c) => c switch
    {
        DrawerCorner.TopLeft => "top left",
        DrawerCorner.TopRight => "top right",
        DrawerCorner.BottomLeft => "bottom left",
        _ => "bottom right",
    };

    private static string FlowName(DrawerFlow f) => f == DrawerFlow.Row ? "row" : "column";

    [RelayCommand]
    private void CopyPrompt()
    {
        try
        {
            Clipboard.SetText(PromptText);
            Status = SetupCopy.ChatPromptCopied;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            Status = "The clipboard is busy. Try Copy prompt again.";   // another app holds the clipboard
        }
    }

    [RelayCommand]
    private void Preview()
    {
        if (_desk is not { } verdict) return;
        if (_previewDesk is null) { Status = SetupCopy.ChatNoDeskService; return; }
        var answer = _previewDesk(verdict);
        Status = answer.Status == DeskAnswer.Previewing ? SetupCopy.ChatDeskShown : Refusal(answer);
    }

    [RelayCommand]
    private void UseRules()
    {
        if (_rules is not { } rules) return;
        Status = _useRules(rules, _ruleNames) is { } said ? StreamerMode.Mask.Paths(said) : SetupCopy.ChatRulesShown;
    }

    [RelayCommand]
    private void UseDrawerSettings()
    {
        if (_drawerLook is not { } look) return;
        if (_useDrawerSettings is null) { Status = SetupCopy.ChatNoDeskService; return; }
        Status = _useDrawerSettings(look) is { } said
            ? StreamerMode.Mask.Paths(said)
            : look.Arrange is null ? SetupCopy.ChatDrawerSettingsDone : SetupCopy.ChatDrawerSettingsArranged;
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();

    /// <summary>Why a preview was not shown, as a line: the first reason's field and words, with a path hidden while streamer mode is on.</summary>
    private static string Refusal(DeskAnswer answer)
    {
        var first = PastedSetup.Findings(answer.Reasons ?? [], []).FirstOrDefault();
        string why = first is null ? "it could not be shown" : $"{first.Field} {first.Text}";
        return StreamerMode.Mask.Paths(SetupCopy.ChatNotShown(why));
    }
}
