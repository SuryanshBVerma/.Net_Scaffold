// ═══════════════════════════════════════════════════════════════════════════
// env.js — Runtime environment configuration
// ═══════════════════════════════════════════════════════════════════════════
//
// LEARNING — Why this file exists (the runtime config pattern):
//
//   Angular builds are COMPILE-TIME — all values are baked into the JS bundle.
//   This means you cannot change API URLs after the build without rebuilding.
//
//   The solution: serve this tiny file from the web server at startup,
//   BEFORE the Angular bundle loads. Set window.__env here.
//   Angular reads it at runtime via the interceptor.
//
//   How Aspire uses this in development:
//     Aspire sets CATALOG_API_URL as a process env var when starting `npm run start`.
//     The Angular CLI dev server rewrites this file on startup via a custom
//     webpack plugin or a prebuild script (see scripts/generate-env.js).
//
//   How production Docker uses this:
//     The nginx startup script (docker-entrypoint.sh) generates this file
//     from environment variables before nginx starts, so the container can
//     be configured at `docker run` time without rebuilding the image.
//
//   This is THE canonical pattern for runtime config in container-deployed SPAs.
//
// ═══════════════════════════════════════════════════════════════════════════
(function (window) {
  window.__env = window.__env || {};

  // Overridden at container startup by Aspire (development) or nginx entrypoint (production).
  // Default points to the local ProductCatalog dev server port.
  window.__env.CATALOG_API_URL = 'http://localhost:5001';
})(window);
