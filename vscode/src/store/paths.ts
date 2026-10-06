import * as os from 'node:os';
import * as path from 'node:path';

/**
 * Data directory: the `sanduhrTime.dataDir` setting if non-empty, then
 * `SANDUHR_TIME_DIR`, then `~/.sanduhr/time`.
 */
export function dataDir(settingValue?: string, env: NodeJS.ProcessEnv = process.env): string {
  if (settingValue && settingValue.trim() !== '') return settingValue.trim();
  const fromEnv = env.SANDUHR_TIME_DIR;
  if (fromEnv && fromEnv.trim() !== '') return fromEnv.trim();
  return path.join(os.homedir(), '.sanduhr', 'time');
}

export const spoolDir = (dir: string): string => path.join(dir, 'spool');
export const claudeDir = (dir: string): string => path.join(dir, 'claude');
export const daysDir = (dir: string): string => path.join(dir, 'days');
export const stateDir = (dir: string): string => path.join(dir, 'state');
