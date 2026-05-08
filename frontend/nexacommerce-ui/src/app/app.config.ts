// ═══════════════════════════════════════════════════════════════════════════
// app.config.ts — Application-level providers
// ═══════════════════════════════════════════════════════════════════════════
//
// LEARNING — Standalone bootstrap (Angular 17+):
//   The traditional AppModule is replaced by ApplicationConfig.
//   provideHttpClient() with withFetch() uses the Fetch API (not XHR) for
//   smaller bundle size and better streaming support.
//
//   withInterceptors() is the standalone alternative to HTTP_INTERCEPTORS.
//   Here we wire up the apiBaseUrl interceptor so every HttpClient request
//   automatically gets the correct base URL injected by Aspire.
//
// ═══════════════════════════════════════════════════════════════════════════
import { ApplicationConfig } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';

import { routes } from './app.routes';
import { apiBaseUrlInterceptor } from './core/interceptors/api-base-url.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideRouter(routes),
    // LEARNING — withFetch(): uses browser Fetch API instead of XMLHttpRequest.
    // withInterceptors(): functional interceptors (no class boilerplate).
    provideHttpClient(withFetch(), withInterceptors([apiBaseUrlInterceptor])),
  ],
};
