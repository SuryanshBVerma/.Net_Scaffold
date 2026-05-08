// ═══════════════════════════════════════════════════════════════════════════
// api-base-url.interceptor.ts — Aspire environment variable bridge
// ═══════════════════════════════════════════════════════════════════════════
//
// LEARNING — How Aspire injects the backend URL into a frontend npm app:
//
//   In AppHost/Program.cs:
//     builder.AddNpmApp("frontend", "../../frontend/nexacommerce-ui")
//       .WithEnvironment("CATALOG_API_URL", catalogApi.GetEndpoint("http"))
//
//   Aspire calls `GetEndpoint("http")` at startup, resolves the actual port
//   assigned to the product-catalog service, and injects it as an env var
//   when it starts the npm process.
//
//   Inside the Angular dev server (ng serve), environment variables are NOT
//   directly accessible in the browser bundle — only in Node.js (the server
//   process). The Angular CLI supports a server-side proxy or compile-time
//   replacements via `environment.ts`.
//
//   For this placeholder we use the compile-time pattern:
//     • In development: Angular CLI proxy (`proxy.conf.json`) forwards /api/** → catalog
//     • In production: the Dockerfile sets the real base URL via nginx config
//
//   The interceptor reads from a global `window.__env` object which is
//   populated by `env.js` — a tiny script served by the Angular app that
//   is rendered at request time (not build time), so the URL can change
//   without rebuilding the Angular bundle.
//
//   This is the canonical pattern for runtime configuration in SPAs:
//     index.html → <script src="env.js"> → sets window.__env.CATALOG_API_URL
//     Angular app reads window.__env.CATALOG_API_URL here
//     Aspire writes env.js at startup with the correct URL
//
// ═══════════════════════════════════════════════════════════════════════════
import { HttpInterceptorFn } from '@angular/common/http';

// LEARNING — Functional interceptors (Angular 15+):
//   No class, no injection tokens. Just a function.
//   Replaces the verbose class-based HTTP_INTERCEPTORS pattern.
export const apiBaseUrlInterceptor: HttpInterceptorFn = (req, next) => {
  // Read runtime config injected by Aspire (or nginx in production).
  // Falls back to localhost for developers who run ng serve without Aspire.
  const catalogApiUrl =
    (window as Window & { __env?: { CATALOG_API_URL?: string } }).__env?.CATALOG_API_URL
    ?? 'http://localhost:5001';

  // Only prepend base URL for relative paths (i.e. our own API calls).
  // Leave absolute URLs (e.g. external CDNs) untouched.
  if (req.url.startsWith('/')) {
    const apiReq = req.clone({ url: `${catalogApiUrl}${req.url}` });
    return next(apiReq);
  }

  return next(req);
};
