import Foundation

/// Item 75, slice E: the Build with Claude prompt. New Mod copies it to the clipboard for Claude
/// Code or Claude Desktop. It explains what a mod is, what it may draw, the looks Sanduhr already
/// speaks and the rules, and asks for the mod to be built in the session. Apart from the idea typed
/// in, it is fixed text: it names no folder on this Mac and carries no setting.
enum ModPrompt {
    /// The idea is one line in the prompt, at most this long.
    static let maxIdea = 400

    /// The idea as it goes into the prompt: one line, trimmed, capped.
    static func idea(_ raw: String) -> String {
        let line = raw.split(whereSeparator: \.isNewline).joined(separator: " ")
            .trimmingCharacters(in: .whitespaces)
        return line.count > maxIdea ? String(line.prefix(maxIdea)).trimmingCharacters(in: .whitespaces) : line
    }

    /// With `canPropose` (Let agents propose mods is on), the prompt ends by sending the mod to
    /// Sanduhr with `propose_mod`.
    static func text(idea raw: String, canPropose: Bool = false) -> String {
        let idea = idea(raw)
        let ask = idea.isEmpty
            ? "Build me a Claude Code mod. Ask me what it should show or do before you start."
            : "Build me a Claude Code mod: \(idea)"
        let fonts = LetterStyle.allCases.map(\.rawValue).joined(separator: ", ")
        return """
        \(ask)

        What a mod is
        - A Claude Code plugin: a folder with .claude-plugin/plugin.json (name in lowercase with hyphens, version, description) and hooks/hooks.json, whose "modules" names one ES module.
        - The module exports register(on, options). It reaches everything outside itself through $: no Node APIs, no require, no import(). Keep $ calls direct, so `claude plugin validate --json` can list them without running the mod.
        - Settings someone can change go in plugin.json "userConfig" (type, title, description, options, default). They arrive as register's options, read once when the mod loads.
        - The mod API is new and changes between releases. Claude Code writes the types for this build to .claude-plugin/types/claude-code/index.d.ts beside a mod it loads; trust those over memory.

        What it may draw
        - The band above the prompt, dim text after the prompt hint, a toast, a pane, slash commands.
        - Text takes a color (a theme key or #hex), bold, italic, dim and underline. There is no gradient or shimmer prop: a gradient is one color per character, and motion is a repaint on a $.clock timer, every 80 ms while something moves and once a second otherwise.
        - Fold to one line when the terminal is too narrow, and keep the band to a few rows.

        Looks, in the shape Sanduhr already uses for its band and statusline
        - A look is {"ink": [colors], "font": name, "bold", "italic", "dim", "underline"}: ink is one #hex color, or 2 to 4 stops of a gradient across the text.
        - font is a Unicode letter style: \(fonts).
        - Effects, each used sparingly: a sweep (a three-character light across a bar when it crosses a line), a shimmer (soon to change), a glow (needs attention), a pulse (waiting on me).
        - Add a "motion" option (on or off) in userConfig; off keeps everything still.

        Rules
        - No network unless I asked for it. No secrets, tokens or keys in its files or its output.
        - Read-only by default: no writing files, running processes or changing settings unless the idea needs it, and then say so first.
        - Write tests beside it and run them with `claude plugin test`; stub every $ call they make.
        - Run `claude plugin validate --json` when it's done and tell me what it reports the mod can do.

        Where to build it
        - Build it in this session, in the folder Claude Code offers for a mod made in a session (it asks to turn on hot reloading), so I can watch it as you write it.
        - Don't add it to my settings or install it anywhere else. When it works, tell me its name and what it draws.\(canPropose ? "\n- Then send it to Sanduhr with its propose_mod tool (the files inline, never a folder), so I can review and keep it, and check get_mod_status for my answer." : "")
        """
    }
}
