#!/bin/sh
# ═══════════════════════════════════════════════════════════════════════════
# docker-entrypoint.sh — Runtime environment variable injection
# ═══════════════════════════════════════════════════════════════════════════
#
# LEARNING — This script is placed in /docker-entrypoint.d/ which nginx's
# official Docker image executes automatically before starting nginx.
#
# It generates src/assets/env.js from environment variables so the Angular
# app receives the correct backend URL without needing a rebuild.
#
# Usage:
#   docker run -e CATALOG_API_URL=https://api.prod.example.com nexacommerce/ui
#
# ═══════════════════════════════════════════════════════════════════════════
set -e

CATALOG_API_URL="${CATALOG_API_URL:-http://localhost:5001}"

cat > /usr/share/nginx/html/assets/env.js << EOF
(function (window) {
  window.__env = window.__env || {};
  window.__env.CATALOG_API_URL = '${CATALOG_API_URL}';
})(window);
EOF

echo "[env] CATALOG_API_URL → ${CATALOG_API_URL}"
