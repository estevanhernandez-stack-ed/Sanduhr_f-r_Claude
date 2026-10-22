/**
 * Incremental line reading shared by the transcript readers: read a bounded chunk from a
 * byte offset, extended to the end of a line, and split it into complete lines.
 */
import * as fs from 'node:fs';

const EXTEND_CHUNK = 1024 * 1024;

/** Split a buffer into complete lines with their byte offsets (relative to the buffer). */
export function splitLines(buf: Buffer): { start: number; end: number }[] {
  const out: { start: number; end: number }[] = [];
  let start = 0;
  for (;;) {
    const nl = buf.indexOf(0x0a, start);
    if (nl === -1) break;
    out.push({ start, end: nl });
    start = nl + 1;
  }
  return out;
}

/** Read `[offset, offset+budget)` extended to a newline. Returns the bytes and whether any remain. */
export function readChunk(fd: number, offset: number, size: number, budget: number): Buffer {
  const want = Math.min(Math.max(budget, 1), size - offset);
  let buf = Buffer.alloc(want);
  let got = fs.readSync(fd, buf, 0, want, offset);
  buf = buf.subarray(0, got);
  // A line longer than the budget must still be read whole, or the pass would never advance.
  while (buf.indexOf(0x0a) === -1 && offset + got < size) {
    const more = Math.min(EXTEND_CHUNK, size - offset - got);
    const extra = Buffer.alloc(more);
    const n = fs.readSync(fd, extra, 0, more, offset + got);
    if (n <= 0) break;
    buf = Buffer.concat([buf, extra.subarray(0, n)]);
    got += n;
  }
  if (buf.length > want) {
    // Extension overshoots in whole chunks; keep only through the end of the line that crossed the budget.
    const nl = buf.indexOf(0x0a, want - 1);
    if (nl !== -1) buf = buf.subarray(0, nl + 1);
  }
  return buf;
}
