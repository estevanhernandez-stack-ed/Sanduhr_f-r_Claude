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
        DismissButton.Click += (_, _) => DismissRequested?.Invoke();
        ApplyButton.Click += (_, _) =>
        {
            if (Model?.CanApply == true) ApplyRequested?.Invoke();
        };
    }

    /// <summary>The bar's buttons, for the window's key handling and for tests.</summary>
    public Button Apply => ApplyButton;
    public Button Dismiss => DismissButton;

    /// <summary>Shows a model: the name, the note, the lines, the fonts still missing, and whether Apply is available.</summary>
    public void SetModel(DeskReviewModel model)
    {
        Model = model;
        Title.Text = model.Title;
        AutomationProperties.SetName(Title, model.Title);
        Note.Text = model.Note ?? "";
        Note.Visibility = model.Note is null ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(Note, model.Note ?? "");
        Changes.ItemsSource = model.Changes;

        Fonts.Children.Clear();
        foreach (var font in model.MissingFonts) Fonts.Children.Add(FontLine(font));
        Fonts.Visibility = model.MissingFonts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        Warnings.Children.Clear();
        foreach (var w in model.Warnings)
        {
            var line = new TextBlock { Text = w, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 3) };
            line.SetResourceReference(TextBlock.ForegroundProperty, "Sanduhr.Brush.TextDim");
            AutomationProperties.SetName(line, w);
            Warnings.Children.Add(line);
        }
        Warnings.Visibility = model.Warnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        DeskOff.Text = model.DeskOff ? DeskReviewCopy.DeskOffNote : "";
        DeskOff.Visibility = model.DeskOff ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(DeskOff, DeskOff.Text);

        Keys.Text = model.Keys;
        AutomationProperties.SetName(Keys, model.Keys);
        ApplyButton.IsEnabled = model.CanApply;
        AutomationProperties.SetHelpText(ApplyButton, DeskReviewCopy.ApplyHelp(model.CanApply));
    }

    private UIElement FontLine(DeskReviewFont font)
    {
        var text = new TextBlock { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Sanduhr.Brush.Text");
        AutomationProperties.SetName(text, font.Line);
        if (font.Link is null)
        {
            text.Inlines.Add(new Run(font.Line));
            return text;
        }
        text.Inlines.Add(new Run($"{font.Family} is not installed. Get it from "));
        var link = new Hyperlink(new Run(font.Link)) { ToolTip = "Opens in your browser. Sanduhr downloads nothing." };
        link.SetResourceReference(TextElement.ForegroundProperty, "Sanduhr.Brush.Accent");
        AutomationProperties.SetName(link, $"Open the download page for {font.Family}");
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
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(safe) { UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No browser to open: the link is on the bar for the user to copy.
        }
    }
}
