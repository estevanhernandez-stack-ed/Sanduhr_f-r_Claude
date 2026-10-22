import { describe, expect, it } from 'vitest';
import { execWithPatch, TWO_FILE_PATCH } from './fixtures';
import { findPatches, parsePatch } from './patch';

describe('parsePatch', () => {
  it('counts + and - lines per file, never context or hunk headers', () => {
    expect(parsePatch(TWO_FILE_PATCH)).toEqual([
      { path: 'src/a.ts', added: 2, removed: 1 },
      { path: 'src/new.ts', added: 3, removed: 0 },
    ]);
  });

  it('a delete counts 0 lines, a move names its destination, End of File is a marker', () => {
    const patch = [
      '*** Begin Patch',
      '*** Delete File: old/gone.ts',
      '*** Update File: src/from.ts',
      '*** Move to: src/to.ts',
      '@@',
      '-a',
      '+b',
      '*** End of File',
      '*** End Patch',
    ].join('\r\n');
    expect(parsePatch(patch)).toEqual([
      { path: 'old/gone.ts', added: 0, removed: 0 },
      { path: 'src/to.ts', added: 1, removed: 1 },
    ]);
  });

  it('a minus line inside an Add File is content, not a removal', () => {
    expect(parsePatch('*** Begin Patch\n*** Add File: x.md\n+- item\n+-\n*** End Patch')).toEqual([{ path: 'x.md', added: 2, removed: 0 }]);
  });
});

describe('findPatches', () => {
  it('reads a raw patch, a patch quoted inside exec code, and several patches in one input', () => {
    const want = parsePatch(TWO_FILE_PATCH);
    expect(findPatches(TWO_FILE_PATCH)).toEqual(want);
    expect(findPatches(execWithPatch(TWO_FILE_PATCH))).toEqual(want);
    expect(findPatches(JSON.stringify({ command: ['apply_patch', TWO_FILE_PATCH] }))).toEqual(want);
    expect(findPatches(execWithPatch(TWO_FILE_PATCH) + execWithPatch(TWO_FILE_PATCH))).toHaveLength(4);
  });

  it('keeps escaped content intact while unescaping once', () => {
    const patch = '*** Begin Patch\n*** Add File: q.ts\n+const s = "a\\nb";\n*** End Patch';
    expect(findPatches(execWithPatch(patch))).toEqual([{ path: 'q.ts', added: 1, removed: 0 }]);
  });

  it('finds nothing in an input without a patch', () => {
    expect(findPatches('const x = await tools.shell("ls");')).toEqual([]);
  });
});
