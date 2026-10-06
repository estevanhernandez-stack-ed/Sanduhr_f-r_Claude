// Runs inside the isolated test VS Code (see panel-screenshot.mjs). Opens the Time panel,
// waits for the first merge to render, signals the parent to capture the window, then waits
// to be released.
const fs = require('node:fs');
const path = require('node:path');

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

exports.run = async function run() {
  // eslint-disable-next-line @typescript-eslint/no-require-imports
  const vscode = require('vscode');
  const sig = process.env.SANDUHR_SIGNAL_DIR;
  const ext = vscode.extensions.getExtension('626labs.sanduhr-time');
  if (!ext) throw new Error('sanduhr-time not found in the test host');
  await ext.activate();
  await sleep(3000); // first merge
  await vscode.commands.executeCommand('sanduhrTime.openPanel');
  await vscode.commands.executeCommand('workbench.action.closeSidebar');
  await vscode.commands.executeCommand('workbench.action.closeAuxiliaryBar');
  await vscode.commands.executeCommand('notifications.hideToasts');
  await sleep(3500);
  fs.writeFileSync(path.join(sig, 'ready'), '1');
  const deadline = Date.now() + 60_000;
  while (!fs.existsSync(path.join(sig, 'done')) && Date.now() < deadline) await sleep(300);
};
