using System.Windows;
using Sanduhr.App.ViewModels;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>
/// The running app's side of the live review (spec, the agent-designed Desk): draws a design under review on the Desk
/// (<see cref="DeskController.ShowPreview"/>) and the widget (<see cref="WidgetViewModel.PreviewTheme"/>), both from memory, shows the
/// floating <see cref="DeskReviewWindow"/>, and after a design is applied points the Desk, the theme strip and an open Settings
/// window at what was written. Dismiss and Apply come back through the callbacks the service gave.
/// </summary>
internal sealed class DeskDesignHost : IDeskDesignHost, IDisposable
{
    private readonly DeskController _desk;
    private readonly Func<WidgetViewModel?> _widget;
    private readonly Action _settingsFollow;
    private DeskReviewWindow? _window;
    private Action _apply = () => { };
    private Action _dismiss = () => { };

    /// <param name="desk">The Desk.</param>
    /// <param name="widget">The widget's view model (theme preview and apply); null before it exists.</param>
    /// <param name="settingsFollow">Re-reads an open Settings window's Desk page and its designs.</param>
    public DeskDesignHost(DeskController desk, Func<WidgetViewModel?> widget, Action settingsFollow)
    {
        _desk = desk;
        _widget = widget;
        _settingsFollow = settingsFollow;
    }

    public void ShowPreview(DeskDesignSettings settings, ThemeDefinition? theme, DeskReviewModel model, Action apply, Action dismiss)
    {
        _apply = apply;
        _dismiss = dismiss;
        if (_widget() is { } widget)
        {
            if (theme is not null) widget.PreviewTheme(theme);
            else widget.EndPreview();
        }
        _desk.ShowPreview(settings);

        if (_window is null)
        {
            _window = new DeskReviewWindow();
            _window.ApplyRequested += () => _apply();
            _window.DismissRequested += () => _dismiss();
            // Closed some other way (Alt+F4): that is a Dismiss, or the preview would stay on the desktop with no way to end it.
            _window.Closed += (s, _) =>
            {
                if (!ReferenceEquals(_window, s)) return;
                _window = null;
                _dismiss();
            };
            _window.SetModel(model);
            // On the monitor the design changes, not always the primary (#319).
            _window.Target = _desk.ReviewScreen(settings);
            _window.Show();
            _window.Activate();
        }
        else
        {
            _window.SetModel(model);
            _window.Target = _desk.ReviewScreen(settings);
        }
    }

    public void EndPreview()
    {
        CloseWindow();
        _widget()?.EndPreview();
        _desk.EndPreview();
    }

    public void Applied(string? themeKey)
    {
        CloseWindow();
        if (_widget() is { } widget)
        {
            widget.ReloadUserThemes();
            if (themeKey is not null) widget.ApplyThemeByKey(themeKey);
            else widget.EndPreview();
        }
        // What was written is read again; a preview, if one was up, gives way to it.
        if (_desk.Previewing) _desk.EndPreview();
        else _desk.Reload();
        _settingsFollow();
    }

    private void CloseWindow()
    {
        var w = _window;
        _window = null;
        if (w is null) return;
        try { w.Close(); }
        catch (InvalidOperationException) { /* already closing */ }
    }

    public void Dispose() => CloseWindow();
}
