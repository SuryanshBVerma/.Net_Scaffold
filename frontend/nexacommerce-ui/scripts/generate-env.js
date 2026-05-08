// ═══════════════════════════════════════════════════════════════════════════
// generate-env.js — Aspire startup script
// ═══════════════════════════════════════════════════════════════════════════
//
// LEARNING — This script bridges Aspire process env vars → Angular runtime config.
//
//   Aspire injects CATALOG_API_URL as a process environment variable when it
//   starts `npm run start`. But Angular (browser code) can't read process.env.
//
//   This script runs BEFORE Angular starts (see package.json "prestart" hook):
//     1. Reads process.env.CATALOG_API_URL
//     2. Writes src/assets/env.js with the actual URL
//     3. Angular dev server serves the updated env.js
//     4. Browser loads env.js → sets window.__env.CATALOG_API_URL
//     5. api-base-url.interceptor.ts reads it → HTTP requests go to the right URL
//
//   In production (Docker + nginx), the nginx entrypoint script does the same
//   thing with the env vars passed to `docker run`. The Angular bundle doesn't
//   need to know which environment it's running in.
//
// ═══════════════════════════════════════════════════════════════════════════
const fs   = require('fs');
const path = require('path');

const catalogApiUrl = process.env.CATALOG_API_URL ?? 'http://localhost:5001';

const content = `(function (window) {
  window.__env = window.__env || {};
  window.__env.CATALOG_API_URL = '${catalogApiUrl}';
})(window);
`;

const outputPath = path.join(__dirname, '..', 'src', 'assets', 'env.js');
fs.writeFileSync(outputPath, content, 'utf8');
console.log(`[env] CATALOG_API_URL → ${catalogApiUrl}`);
