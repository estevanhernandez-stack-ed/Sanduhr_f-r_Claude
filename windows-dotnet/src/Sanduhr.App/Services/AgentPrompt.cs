using System.IO;
using System.Reflection;

namespace Sanduhr.App.Services;

/// <summary>
/// Loads the theme-builder agent prompt that the Settings → Themes
/// "Copy agent prompt" button puts on the clipboard. The markdown is shipped as
/// an <c>EmbeddedResource</c> inside the App assembly (see Sanduhr.App.csproj) so
/// it resolves in a packaged MSIX build where the repo's <c>docs/</c> tree does
/// not exist — never read from a repo-relative path at runtime.
/// </summary>
internal static class AgentPrompt
{
    private const string ResourceName = "Sanduhr.App.AGENT_PROMPT.md";

    /// <summary>The embedded prompt text, or <see cref="Fallback"/> if the
    /// resource can't be loaded (defensive — the embed should always be present).</summary>
    public static string Load()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream(ResourceName);
            if (stream is null)
                return Fallback;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (IOException)
        {
            return Fallback;
        }
    }

    /// <summary>Minimal inline prompt used only if the embedded resource is
    /// missing — mirrors <c>settings_dialog._fallback_prompt</c>.</summary>
    public const string Fallback =
        "Design a Sanduhr widget theme JSON. Required hex fields: " +
        "name, bg, glass, glass_on_mica, title_bg, border, footer_bg, " +
        "bar_bg, text, text_secondary, text_dim, text_muted, accent, " +
        "pace_marker, sparkline. Optional: glass_alpha (0.70-0.90), " +
        "border_alpha, border_tint, accent_bloom {blur, alpha}, " +
        "inner_highlight {color, alpha}. Dark base colors only. " +
        "Return valid JSON only, no markdown.";
}
