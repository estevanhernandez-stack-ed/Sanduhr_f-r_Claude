import { describe, expect, it } from 'vitest';
import { hashPath } from './hash';

describe('hashPath', () => {
  it('returns 24 hex chars', () => {
    expect(hashPath('/fixture/app/a.ts', 'salt', 'linux')).toMatch(/^[0-9a-f]{24}$/);
  });

  it('is stable and salt-dependent', () => {
    const a = hashPath('/fixture/app/a.ts', 'salt1', 'linux');
    expect(hashPath('/fixture/app/a.ts', 'salt1', 'linux')).toBe(a);
    expect(hashPath('/fixture/app/a.ts', 'salt2', 'linux')).not.toBe(a);
  });

  it('win32: backslash, forward slash, drive-letter case and path case all hash identically', () => {
    const h = hashPath('C:\\fixture\\App\\Src\\a.ts', 's', 'win32');
    expect(hashPath('c:/fixture/app/src/a.ts', 's', 'win32')).toBe(h);
    expect(hashPath('C:/Fixture/APP/src/A.ts', 's', 'win32')).toBe(h);
  });

  it('win32: dot segments resolve away', () => {
    expect(hashPath('C:\\fixture\\app\\..\\app\\a.ts', 's', 'win32')).toBe(
      hashPath('C:\\fixture\\app\\a.ts', 's', 'win32'),
    );
  });

  it('posix: case matters', () => {
    expect(hashPath('/fixture/App/a.ts', 's', 'linux')).not.toBe(
      hashPath('/fixture/app/a.ts', 's', 'linux'),
    );
  });

  it('posix: dot segments and duplicate slashes resolve away', () => {
    expect(hashPath('/fixture//app/../app/a.ts', 's', 'linux')).toBe(
      hashPath('/fixture/app/a.ts', 's', 'linux'),
    );
  });
});
