import { createHash } from 'node:crypto';
import * as path from 'node:path';

/**
 * The one path hash both the spool writer and the Claude reader use.
 * Resolve, forward slashes; on win32 lowercase the whole path (drive letter included,
 * since the filesystem is case-insensitive); then sha256(salt + path), first 24 hex chars.
 * `platform` is injectable so tests cover both behaviors on any OS.
 */
export function hashPath(
  absPath: string,
  salt: string,
  platform: NodeJS.Platform = process.platform,
): string {
  const win = platform === 'win32';
  let p = (win ? path.win32 : path.posix).resolve(absPath).split('\\').join('/');
  if (win) p = p.toLowerCase();
  return createHash('sha256')
    .update(salt + p)
    .digest('hex')
    .slice(0, 24);
}
