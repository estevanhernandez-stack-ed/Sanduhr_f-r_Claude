namespace Sanduhr.Core;

/// <summary>
/// What the pointer is doing on the Launcher's buttons, as one small machine so the mouse and the touch rules
/// are tested without a window. The view feeds it the button index under the pointer (null for none) and
/// reads <see cref="Glow"/> and <see cref="Dip"/> back. Each call that can launch returns the index to launch.
///
/// Mouse: hovering a button glows it; pressing it dips it (the dip ends if the pointer slides off); letting go
/// on the same button launches. Touch: touch-down on a button glows it and dips it; touch-up still on that
/// button launches; dragging out cancels for the rest of that touch, so moving back in does not re-arm it.
/// </summary>
public sealed class LauncherPointer
{
    private int? _hover;
    private int? _mouseArmed;
    private int? _touchArmed;
    private bool _touchOut;

    /// <summary>The button to draw glowing now: the touched one while a touch is on it, else the hovered one.</summary>
    public int? Glow => _touchArmed is { } t ? (_touchOut ? null : t) : _hover;

    /// <summary>The button to draw dipped now: the pressed one while the pointer is still on it.</summary>
    public int? Dip => _touchArmed is { } t ? (_touchOut ? null : t) : (_mouseArmed is { } m && m == _hover ? m : null);

    /// <summary>True while a touch is down (the view captures it).</summary>
    public bool Touching => _touchArmed is not null;

    /// <summary>The mouse moved over the Launcher; <paramref name="index"/> is the button under it, or null.</summary>
    public void MouseMove(int? index) => _hover = index;

    /// <summary>The mouse left the Launcher: nothing is hovered, and a press in flight is dropped.</summary>
    public void MouseLeave()
    {
        _hover = null;
        _mouseArmed = null;
    }

    /// <summary>The left button went down; a press on a button arms it.</summary>
    public void MouseDown(int? index)
    {
        _hover = index;
        _mouseArmed = index;
    }

    /// <summary>The left button came up. Returns the button to launch: the one that was pressed, if the pointer is still on it.</summary>
    public int? MouseUp(int? index)
    {
        _hover = index;
        var armed = _mouseArmed;
        _mouseArmed = null;
        return armed is { } a && index == a ? a : null;
    }

    /// <summary>A finger went down; on a button it arms the touch (glow and dip), elsewhere nothing happens.</summary>
    public void TouchDown(int? index)
    {
        _touchArmed = index;
        _touchOut = false;
        _hover = null;
    }

    /// <summary>The finger moved; leaving the armed button cancels this touch for good.</summary>
    public void TouchMove(int? index)
    {
        if (_touchArmed is { } a && index != a) _touchOut = true;
    }

    /// <summary>The finger lifted. Returns the button to launch: the touched one, if the finger never left it.</summary>
    public int? TouchUp(int? index)
    {
        var armed = _touchArmed;
        bool wasOut = _touchOut;
        Cancel();
        return armed is { } a && !wasOut && index == a ? a : null;
    }

    /// <summary>The touch was lost (capture taken, window gone): glow and dip end, nothing launches.</summary>
    public void Cancel()
    {
        _touchArmed = null;
        _touchOut = false;
    }

    /// <summary>The buttons changed under the pointer (a redraw): forget every press and hover.</summary>
    public void Reset()
    {
        _hover = null;
        _mouseArmed = null;
        Cancel();
    }
}
