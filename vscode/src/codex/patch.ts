/**
 * Codex `apply_patch` bodies: which files a patch touches and how many lines it adds and
 * removes. Only paths and counts leave this module; patch text is never returned or stored.
 *
 * The format:
 *   *** Begin Patch
 *   *** Add File: <path>        then `+` lines, the new file's content
 *   *** Update File: <path>     then `@@` hunk headers, ` ` context, `-` removed and `+` added lines
 *   *** Move to: <path>         optional, right after an Update header
 *   *** Delete File: <path>     no content follows, so it counts 0 lines
 *   *** End of File             optional marker inside a hunk
 *   *** End Patch
 */

export interface PatchFile {
  /** The path as the patch names it (absolute, or relative to the turn's cwd). The destination of a move. */
  path: string;
  added: number;
  removed: number;
}

const BEGIN = '*** Begin Patch';
const END = '*** End Patch';
const HEADER = /^\*\*\* (Add|Update|Delete) File: (.+)$/;
const MOVE = /^\*\*\* Move to: (.+)$/;

/** Files and line counts of one patch body (the text from `*** Begin Patch` on). */
export function parsePatch(patch: string): PatchFile[] {
  const files: PatchFile[] = [];
  let current: (PatchFile & { op: string }) | undefined;
  for (const rawLine of patch.split('\n')) {
    const line = rawLine.endsWith('\r') ? rawLine.slice(0, -1) : rawLine;
    const header = HEADER.exec(line);
    if (header) {
      current = { op: header[1], path: header[2].trim(), added: 0, removed: 0 };
      files.push(current);
      continue;
    }
    if (line.startsWith('***')) {
      const move = MOVE.exec(line);
      if (move && current) current.path = move[1].trim();
      continue; // Begin/End Patch, End of File
    }
    if (!current || current.op === 'Delete') continue;
    if (line.startsWith('+')) current.added++;
    else if (line.startsWith('-') && current.op === 'Update') current.removed++;
  }
  return files.map(({ path, added, removed }) => ({ path, added, removed }));
}

/** Undo one level of JSON/JS string escaping, for a patch quoted inside a tool's input. */
function unescape(s: string): string {
  return s.replace(/\\(u[0-9a-fA-F]{4}|.)/g, (_, c: string) => {
    if (c.length === 5) return String.fromCharCode(parseInt(c.slice(1), 16));
    switch (c) {
      case 'n':
        return '\n';
      case 'r':
        return '\r';
      case 't':
        return '\t';
      default:
        return c; // \" \\ \' \`
    }
  });
}

/**
 * Every patch inside a tool call's input. `apply_patch` sends the patch as the input itself;
 * Codex's `exec` tool (and a shell call's JSON arguments) carry it as a quoted string, with
 * its newlines escaped, so a region with no real newline is unescaped once before parsing.
 */
export function findPatches(input: string): PatchFile[] {
  const out: PatchFile[] = [];
  let from = 0;
  for (;;) {
    const start = input.indexOf(BEGIN, from);
    if (start === -1) break;
    const end = input.indexOf(END, start + BEGIN.length);
    const stop = end === -1 ? input.length : end + END.length;
    const region = input.slice(start, stop);
    out.push(...parsePatch(region.includes('\n') ? region : unescape(region)));
    from = stop;
  }
  return out;
}
