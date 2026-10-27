using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Automation;
using System.Windows.Media;
using Sanduhr.App.Views;
using Sanduhr.Core;

namespace Sanduhr.App.Tests;

/// <summary>
/// Settings v2, slice 4 (4.0 polish, task 8): the dialog's secondary and tertiary buttons and the section expander's toggle answer
/// hover and press, the toggle rings in Accent, and a section with no help text draws no summary line.
/// </summary>
[Collection("SettingsWindow")]
public sealed class SettingsControlStatesTests : AppWindowFixture
{
    private static string? KeyOf(object? value) => value switch
    {
        DynamicResourceExtension d => d.ResourceKey as string,
        _ => null,
    };

    // A loaded template reads a DynamicResource setter back as the extension; if it ever reads back as an expression, the key is
    // reached through its ToString-free path below, so fail loudly instead of passing on nothing.
    private static bool SetsBrush(Trigger t, DependencyProperty property, string key) =>
        t.Setters.OfType<Setter>().Any(s => s.Property == property && KeyOf(s.Value) == key);

    private static bool SetsGlassHover(Trigger t) => SetsBrush(t, Border.BackgroundProperty, "Sanduhr.Brush.GlassHover");

    private static IEnumerable<T> Below<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) yield return t;
            foreach (var deeper in Below<T>(child)) yield return deeper;
        }
    }

    [Fact]
    public void The_dialogs_other_buttons_take_the_dialog_style_that_hovers_and_presses_in_GlassHover() => AppHost.Run(() =>
    {
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
        var dialog = ThemedDialog.BuildConfirm("Title?", "detail", "OK", "Cancel");   // never shown
        foreach (var button in new[] { dialog.SecondaryButton, dialog.TertiaryButton })
        {
            Assert.NotNull(button.Style);
            Assert.Same(dialog.TryFindResource("DialogFlatButton"), button.Style);
            var template = (ControlTemplate)button.Style.Setters.OfType<Setter>().Single(s => s.Property == Control.TemplateProperty).Value;
            var triggers = template.Triggers.OfType<Trigger>().ToList();
            Assert.Contains(triggers, t => t.Property == UIElement.IsMouseOverProperty && SetsGlassHover(t));
            var pressed = triggers.Single(t => t.Property == ButtonBase.IsPressedProperty);
            Assert.True(SetsGlassHover(pressed));
            Assert.True(SetsBrush(pressed, Border.BorderBrushProperty, "Sanduhr.Brush.Accent"));
        }
    });

    private static (Expander Expander, Window Host, ToggleButton Toggle) BuildExpander(Window owner, bool tagNull)
    {
        var expander = new Expander { Style = (Style)owner.FindResource("SectionExpander"), Header = "Title" };
        var host = new Window { Content = expander, ShowActivated = false, WindowStyle = WindowStyle.None, ShowInTaskbar = false, Left = -32000, Top = -32000, Width = 300, Height = 120 };
        host.Show();
        expander.ApplyTemplate();
        var toggle = (ToggleButton)expander.Template.FindName("HeaderSite", expander);
        toggle.ApplyTemplate();
        // HelpText rejects null (its validation allows only strings), so a null Tag cannot arrive through the expander; the line must
        // still collapse if it ever does, so the test clears the toggle's template-bound Tag directly.
        if (tagNull) toggle.Tag = null;
        host.UpdateLayout();
        return (expander, host, toggle);
    }

    [Fact]
    public void The_expander_toggle_answers_hover_and_press_and_rings_in_Accent() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var window = new SettingsWindow(new Sanduhr.App.ViewModels.SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask));
        try
        {
            var (_, host, toggle) = BuildExpander(window, false);
            try
            {
                var triggers = toggle.Template.Triggers.OfType<Trigger>().ToList();
                Assert.Contains(triggers, t => t.Property == UIElement.IsMouseOverProperty && SetsGlassHover(t));
                Assert.Contains(triggers, t => t.Property == ButtonBase.IsPressedProperty && SetsGlassHover(t));
                var focus = triggers.Single(t => t.Property == UIElement.IsKeyboardFocusedProperty);
                Assert.True(SetsBrush(focus, Border.BorderBrushProperty, "Sanduhr.Brush.Accent"));
            }
            finally { host.Close(); }
        }
        finally { window.Close(); }
    });

    [Fact]
    public void A_section_with_no_help_text_draws_no_summary_line() => AppHost.Run(() =>
    {
        var (widget, _) = ScratchWidget("Personal");
        var window = new SettingsWindow(new Sanduhr.App.ViewModels.SettingsViewModel(widget, () => Task.CompletedTask, _ => Task.CompletedTask));
        try
        {
            var (_, host, toggle) = BuildExpander(window, true);
            try
            {
                Assert.Null(toggle.Tag);
                var summary = Below<TextBlock>(toggle).Single(t => t.Style?.Triggers.OfType<DataTrigger>().Any() == true);
                Assert.Equal(Visibility.Collapsed, summary.Visibility);
            }
            finally { host.Close(); }
        }
        finally { window.Close(); }
    });
}
