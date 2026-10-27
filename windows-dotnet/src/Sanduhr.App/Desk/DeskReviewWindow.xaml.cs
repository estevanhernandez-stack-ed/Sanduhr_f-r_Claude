using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Sanduhr.App.Interop;
using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>
/// The live review's window (spec, the agent-designed Desk): <see cref="DeskReviewBar"/> at the top center of the work area of the
/// screen the design changes (<see cref="Target"/>, else the primary), topmost while a design previews. <b>Escape</b> dismisses; <b>Enter</b> applies (while no font is missing), except
/// where Enter already means something: on a focused button or link.
/// </summary>
public sealed partial class DeskReviewWindow : Window
{
    private const double TopGap = 8;

    public DeskReviewWindow()
    {
        InitializeComponent();
        Title = DeskReviewCopy.BarName;
        AutomationProperties.SetName(this, DeskReviewCopy.BarName);
        Bar.ApplyRequested += () => ApplyRequested?.Invoke();
        Bar.DismissRequested += () => DismissRequested?.Invoke();
        SizeChanged += (_, _) => Place();
        Loaded += (_, _) =>
        {
            Place();
            FocusDefault();
        };
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private DeskScreen? _target;

    /// <summary>The screen to open on: the one drawing most of what the design changes (<see cref="DeskController.ReviewScreen"/>).
    /// Null is the primary screen.</summary>
    internal DeskScreen? Target
    {
        get => _target;
        set
        {
            _target = value;
            if (IsLoaded) Place();
        }
    }

    /// <summary>The bar inside.</summary>
    public DeskReviewBar ReviewBar => Bar;

    /// <summary>Apply was chosen (button or Enter).</summary>
    public event Action? ApplyRequested;

    /// <summary>Dismiss was chosen (button or Escape).</summary>
    public event Action? DismissRequested;

    /// <summary>Shows a model and puts the keyboard on the button Enter would choose.</summary>
    public void SetModel(DeskReviewModel model)
    {
        bool wasApplyEnabled = Bar.Apply.IsEnabled;
        Bar.SetModel(model);
        Title = model.BarName ?? DeskReviewCopy.BarName;
        AutomationProperties.SetName(this, Title);   // streamer: notname - the window's fixed title
        if (IsLoaded && Bar.Apply.IsEnabled != wasApplyEnabled) FocusDefault();
    }

    private void FocusDefault()
    {
        // Apply when it is available. While a font is missing the window itself holds the keyboard, so Enter does nothing (it must
        // not dismiss) and Escape still dismisses.
        if (Bar.Apply.IsEnabled) Bar.Apply.Focus();
        else Focus();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (HandleKey(e.Key, Keyboard.FocusedElement)) e.Handled = true;
    }

    /// <summary>What a key does on the bar: Escape dismisses; Enter applies (nothing while a font is missing). True when the key was taken.</summary>
    internal bool HandleKey(Key key, IInputElement? focused)
    {
        if (key == Key.Escape)
        {
            DismissRequested?.Invoke();
            return true;
        }
        if (key is Key.Enter or Key.Return && !EnterBelongsToTheFocus(focused))
        {
            if (Bar.Model?.CanApply == true) ApplyRequested?.Invoke();
            return true;
        }
        return false;
    }

    /// <summary>Enter on a focused button or link chooses it, as in any dialog.</summary>
    private bool EnterBelongsToTheFocus(IInputElement? focused) =>
        ReferenceEquals(focused, Bar.Dismiss) || focused is Hyperlink;

    /// <summary>Top center of the target screen's work area, else the primary's.</summary>
    private void Place()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (_target is { } screen && hwnd != IntPtr.Zero)
        {
            // In physical pixels, per-monitor aware (the screens' rectangles are), so a bar on a monitor of another scale lands
            // where it should. Twice: moving onto a monitor of another scale resizes the window, and the second pass centers that size.
            for (int pass = 0; pass < 2; pass++)
            {
                if (DeskNative.PerMonitor(() => DeskNative.Rect(hwnd)) is not { } now) break;
                var (x, y) = BarOrigin(screen.Work, screen.Scale, now.Width);
                DeskNative.PerMonitor(() => DeskNative.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0,
                    DeskNative.SWP_NOSIZE | DeskNative.SWP_NOZORDER | DeskNative.SWP_NOACTIVATE));
            }
            return;
        }
        var work = SystemParameters.WorkArea;
        Left = work.Left + Math.Max(0, (work.Width - ActualWidth) / 2);
        Top = work.Top + TopGap;
    }

    /// <summary>The bar's top-left, physical pixels: centered across the work area, <see cref="TopGap"/> DIPs below its top.</summary>
    internal static (int X, int Y) BarOrigin(PxRect work, double scale, int widthPx) =>
        (work.X + Math.Max(0, (work.Width - widthPx) / 2), work.Y + (int)Math.Round(TopGap * (scale > 0 ? scale : 1)));
}
