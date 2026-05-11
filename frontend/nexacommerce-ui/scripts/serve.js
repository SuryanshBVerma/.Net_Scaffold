// ═══════════════════════════════════════════════════════════════════════════
// serve.js — Cross-platform ng serve launcher
// ═══════════════════════════════════════════════════════════════════════════
//
// LEARNING — Why this script exists:
//
//   Aspire injects PORT as a process environment variable when it starts
//   the npm process. The Angular CLI flag --port does not read process.env.PORT
//   automatically — it must be passed explicitly on the command line.
//
//   On Linux/macOS you could write:  ng serve --port ${PORT:-4200}
//   On Windows PowerShell that syntax is different, and on CMD it's different again.
//
//   A Node.js launcher script is the cross-platform solution — it runs on all
//   platforms without shell-specific syntax, making the dev setup identical
//   for Windows and Linux developers and for the CI agent.
//
// ═══════════════════════════════════════════════════════════════════════════
const { spawnSync } = require('child_process');

const port = process.env.PORT || '4200';

console.log(`[serve] Starting ng serve on port ${port}`);

const result = spawnSync(
  'npx',
  ['ng', 'serve', '--host', '0.0.0.0', '--port', port],
  { stdio: 'inherit', shell: true }
);

process.exit(result.status ?? 1);
