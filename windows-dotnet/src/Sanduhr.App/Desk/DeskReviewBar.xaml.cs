using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;

using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>
/// The live review's bar (spec, the agent-designed Desk): <see cref="DeskReviewModel"/>'s name, note, "what changed" lines, the
/// fonts still to install with their https links (clickable; the link opens in the browser and Sanduhr never downloads anything),
/// and <b>Apply</b> / <b>Dismiss</b>. Apply is disabled while a font is missing. Every part carries an accessible name.
/// </summary>
public sealed partial class DeskReviewBar : UserControl
{
    /// <summary>Apply was pressed (never raised while a font is missing).</summary>
    public event Action? ApplyRequested;

    /// <summary>Dismiss was pressed.</summary>
    public event Action? DismissRequested;

    /// <summary>Edit was pressed (the drawers review only).</summary>
    public event Action? EditRequested;

    /// <summary>Opens a font's link; the shell opens it in the default browser. Tests replace it.</summary>
    public Action<string> OpenLink { get; set; } = DefaultOpen;

    /// <summary>The model the bar shows now.</summary>
    public DeskReviewModel? Model { get; private set; }

    public DeskReviewBar()
    {
        InitializeComponent();
        MinWidth = 420;
        MaxWidth = 520;
        AutomationProperties.SetName(this, DeskReviewCopy.BarName);
        AutomationProperties.SetName(Frame, DeskReviewCopy.BarName);
        AutomationProperties.SetName(Heading, Heading.Text);
        AutomationProperties.SetName(ChangesHeader, DeskReviewCopy.ChangesName);
        AutomationProperties.SetName(Changes, DeskReviewCopy.ChangesName);
        AutomationProperties.SetName(Fonts, DeskReviewCopy.FontsName);
        AutomationProperties.SetName(DismissButton, DeskReviewCopy.Dismiss);
        AutomationProperties.SetHelpText(DismissButton, "Put your Desk back as it was. Escape does the same");
        AutomationProperties.SetName(ApplyButton, DeskReviewCopy.Apply);
        AutomationProperties.SetName(EditButton, DeskReviewCopy.Edit);
        EditButton.Click += (_, _) => EditRequested?.Invoke();
        DismissButton.Click += (_, _) => DismissRequested?.Invoke();
        ApplyButton.Click += (_, _) =>
        {
            if (Model?.CanApply == true) ApplyRequested?.Invoke();
        };
    }

    /// <summary>The heading the Desk design review shows (the XAML's text).</summary>
    private const string DefaultHeading = "Claude suggests a Desk design";

    /// <summary>The model's <see cref="DeskReviewModel.Option"/> is shown and checked (false when there is none).</summary>
    public bool OptionChecked => Model?.Option is not null && OptionBox.IsChecked == true;

    /// <summary>The option's check box, for tests.</summary>
    public CheckBox Option => OptionBox;

    /// <summary>The bar's buttons, for the window's key handling and for tests.</summary>
    public Button Apply => ApplyButton;
    public Button Dismiss => DismissButton;
    public Button Edit => EditButton;

    /// <summary>Shows a model: the name, the note, the lines, the fonts still missing, and whether Apply is available.</summary>
    public void SetModel(DeskReviewModel model)
    {
        bool sameOption = Model?.Option == model.Option;
        Model = model;
        Heading.Text = model.Heading ?? DefaultHeading;
        AutomationProperties.SetName(Heading, Heading.Text);
        string barName = model.BarName ?? DeskReviewCopy.BarName;
        AutomationProperties.SetName(this, barName);   // streamer: notname - the bar's name is fixed copy or the proposal's own title
        AutomationProperties.SetName(Frame, barName);
        ChangesHeader.Text = model.ChangesTitle ?? DeskReviewCopy.ChangesName;
        AutomationProperties.SetName(ChangesHeader, ChangesHeader.Text);
        AutomationProperties.SetName(Changes, ChangesHeader.Text);
        // An empty title means no header and no list (the drawers result is one line and its buttons).
        var showChanges = model.ChangesTitle != "" ? Visibility.Visible : Visibility.Collapsed;
        ChangesHeader.Visibility = showChanges;
        Changes.Visibility = showChanges;
        // A widget's one choice with Apply ("Make it the companion"), on by default; kept as the user left it while the same bar updates.
        OptionBox.Content = model.Option ?? "";
        AutomationProperties.SetName(OptionBox, model.Option ?? "");   // streamer: notname - the choice text is fixed copy (WidgetReviewCopy.MakeCompanion)
        OptionBox.Visibility = model.Option is null ? Visibility.Collapsed : Visibility.Visible;
        if (!sameOption) OptionBox.IsChecked = true;
        Title.Text = model.Title;
        AutomationProperties.SetName(Title, model.Title);   // streamer: notname - the proposal's own title as Claude wrote it; no account, folder or project is read
        Note.Text = model.Note ?? "";
        Note.Visibility = model.Note is null ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(Note, model.Note ?? "");   // streamer: notname - the proposal's own note as Claude wrote it; no account, folder or project is read
        Changes.ItemsSource = model.Changes;
        Footer.Text = model.Footer ?? "";
        Footer.Visibility = model.Footer is null || model.ChangesTitle == "" ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(Footer, model.Footer ?? "");   // streamer: notname - fixed copy (DrawerReview.RulesFooter), no name in it

        Fonts.Children.Clear();
        foreach (var font in model.MissingFonts) Fonts.Children.Add(FontLine(font));
        Fonts.Visibility = model.MissingFonts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        Warnings.Children.Clear();
        foreach (var w in model.Warnings)
        {
            var line = new TextBlock { Text = w, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 3) };
            line.SetResourceReference(TextBlock.ForegroundProperty, "Sanduhr.Brush.TextDim");
            AutomationProperties.SetName(line, w);   // streamer: notname - lint warning phrasing from DeskReviewCopy, no identifying name
            Warnings.Children.Add(line);
        }
        Warnings.Visibility = model.Warnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        DeskOff.Text = model.DeskOff ? DeskReviewCopy.DeskOffNote : "";
        DeskOff.Visibility = model.DeskOff ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(DeskOff, DeskOff.Text);

        Keys.Text = model.Keys;
        AutomationProperties.SetName(Keys, model.Keys);   // streamer: notname - key hints, fixed copy
        EditButton.Visibility = model.CanEdit ? Visibility.Visible : Visibility.Collapsed;
        ApplyButton.Content = model.ApplyLabel ?? DeskReviewCopy.Apply;
        AutomationProperties.SetName(ApplyButton, (string)ApplyButton.Content);   // streamer: notname - fixed copy, Apply or Undo
        DismissButton.Content = model.DismissLabel ?? DeskReviewCopy.Dismiss;
        AutomationProperties.SetName(DismissButton, (string)DismissButton.Content);   // streamer: notname - fixed copy, Dismiss or Done
        ApplyButton.IsEnabled = model.CanApply;
        AutomationProperties.SetHelpText(ApplyButton, DeskReviewCopy.ApplyHelp(model.CanApply));   // streamer: notname - help text from DeskReviewCopy.ApplyHelp, fixed copy
    }

    private UIElement FontLine(DeskReviewFont font)
    {
        var text = new TextBlock { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Sanduhr.Brush.Text");
        AutomationProperties.SetName(text, font.Line);   // streamer: notname - names a font family, not an identifying name
        if (font.Link is null)
        {
            text.Inlines.Add(new Run(font.Line));
            return text;
        }
        text.Inlines.Add(new Run($"{font.Family} is not installed. Get it from "));
        var link = new Hyperlink(new Run(font.Link)) { ToolTip = "Opens in your browser. Sanduhr downloads nothing." };
        link.SetResourceReference(TextElement.ForegroundProperty, "Sanduhr.Brush.Accent");
        AutomationProperties.SetName(link, $"Open the download page for {font.Family}");   // streamer: notname - names a font family, not an identifying name
        string target = font.Link;
        link.Click += (_, _) => OpenLink(target);
        text.Inlines.Add(link);
        text.Inlines.Add(new Run(", then install it."));
        return text;
    }

    private static void DefaultOpen(string link)
    {
        // Only a link the model already vetted (absolute https) gets here; the shell opens it in the default browser.
        if (DeskReviewModel.SafeLink(link) is not { } safe) return;
        try
        {
            // network: your-browser
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(safe) { UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No browser to open: the link is on the bar for the user to copy.
        }
    }
}
