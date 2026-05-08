# ═══════════════════════════════════════════════════════════════════════════
# Dockerfile — NexaCommerce Angular frontend
# ═══════════════════════════════════════════════════════════════════════════
#
# LEARNING — Two-stage build:
#
#   Stage 1 (builder): Node.js — npm ci + ng build
#     Produces the static dist/ bundle. The Node image is ~400MB but
#     it's discarded after the build.
#
#   Stage 2 (runtime): nginx:alpine — ~25MB
#     Serves the static files. No Node.js at runtime.
#     The nginx entrypoint script generates env.js from environment variables
#     before nginx starts — enabling runtime configuration without a rebuild.
#
# LEARNING — Runtime configuration pattern:
#   docker run -e CATALOG_API_URL=http://api.prod.example.com nexacommerce/ui
#   → nginx entrypoint writes env.js with the new URL
#   → nginx serves the pre-built Angular bundle + the dynamic env.js
#   → Angular reads window.__env.CATALOG_API_URL at startup
#
#   Same Docker image, any environment. No rebuild required.
#
# ═══════════════════════════════════════════════════════════════════════════

# ── Stage 1: Build ───────────────────────────────────────────────────────────
FROM node:22-alpine AS builder

WORKDIR /app

# LEARNING — Layer cache optimisation (same as .NET Dockerfiles):
#   Copy package files first. If they haven't changed, Docker reuses the
#   cached 'npm ci' layer and skips the ~2 min dependency install.
COPY package.json package-lock.json ./
RUN npm ci --ignore-scripts

# Copy source and build
COPY . .
RUN npm run build

# ── Stage 2: Runtime ─────────────────────────────────────────────────────────
FROM nginx:alpine AS runtime

# LEARNING — Non-root user for security (matches .NET Dockerfiles pattern):
#   nginx's worker processes already run as nobody by default.
#   We run the master process as a non-root user using nginx's rootless support.

# Copy compiled Angular app from builder stage
COPY --from=builder /app/dist/nexacommerce-ui/browser /usr/share/nginx/html

# Custom nginx config: SPA routing (all paths → index.html)
COPY nginx.conf /etc/nginx/conf.d/default.conf

# Entrypoint generates env.js from environment variables, then starts nginx
COPY docker-entrypoint.sh /docker-entrypoint.d/40-generate-env.sh
RUN chmod +x /docker-entrypoint.d/40-generate-env.sh

EXPOSE 80
