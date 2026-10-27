using Sanduhr.Core;

namespace Sanduhr.App.Desk;

/// <summary>What the Desk draws while it follows the widget theme (theme match): the theme's look, and the ink made of it.</summary>
internal sealed record DeskThemeDraw(ThemeDeskLook Look, ResolvedInk Ink);

/// <summary>
/// Follow on: the active theme's look (<see cref="ThemeDeskLook.From"/>); off: the Desk's own settings, untouched (theme match, spec
/// "Follow the widget theme"). Pure over its inputs, so the controller, the harness and the tests draw the same look. Sizes, margins,
/// OLED care and the layout are never part of a look.
/// </summary>
internal static class DeskLookResolver
{
    /// <summary>The look to draw, or null when the Desk draws its own (not following, or no theme known yet).</summary>
    public static DeskThemeDraw? For(bool follows, ThemeDefinition? theme)
    {
        if (!follows || theme is null) return null;
        var look = ThemeDeskLook.From(theme);
        // A custom ink of the look's stops, with its shadow on in the theme's shade: DeskInk's own rules decide dark ink and opacities.
        var ink = DeskInk.Resolve(look.InkSettings, null, null) with { ShadowHex = look.Shadow };
        return new DeskThemeDraw(look, ink);
    }

    /// <summary>The view's settings: the look's font (when it has one and this PC has it; else the Desk keeps its own, since a missing
    /// family would fall back to EsteFont Pro) and plate. <paramref name="fontAvailable"/> defaults to all present.</summary>
    public static DeskSettings Settings(DeskSettings own, DeskThemeDraw? draw, Func<string, bool>? fontAvailable = null) => draw is null ? own : own with
    {
        Font = draw.Look.Font is { } f && (fontAvailable?.Invoke(f) ?? true) ? DeskDesignApply.FontSetting(new DeskFontChoice(f)) : own.Font,
        Plate = draw.Look.Plate,
    };

    /// <summary>The frame with the look's plate tint, Now playing gradient, meter colors, track and tick, and the message in the look's
    /// colors with its glow on in the look's glow color (a line's own <c>{ink:}</c> still wins).</summary>
    public static DeskFrame Frame(DeskFrame frame, DeskThemeDraw? draw)
    {
        if (draw is null) return frame;
        var look = draw.Look;
        return frame with
        {
            PlateHex = look.PlateColor,
            NowPlayingStops = look.Gradient,
            MeterStops = look.Bars,
            MeterTrackHex = look.BarTrack,
            PaceHex = look.PaceTick,
            Message = frame.Message is { } m   // streamer: notname - frame.Message is the Desk message frame record
                ? m with { Settings = m.Settings with { Color = string.Join(",", look.Message), Glow = true }, GlowHex = look.Glow }   // streamer: notname - look.Message is a colour list
                : null,
        };
    }
}
