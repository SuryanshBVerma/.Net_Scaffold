# ═══════════════════════════════════════════════════════════════════════════════
# NexaCommerce.ProductCatalog — Multi-Stage Dockerfile
# ═══════════════════════════════════════════════════════════════════════════════
#
# LEARNING — Multi-stage builds:
#   A single Dockerfile can have multiple FROM stages. Docker runs them in order
#   and you COPY artefacts between stages. The final image only contains what
#   you explicitly copy into the last stage.
#
#   Stage 1 "build"   → uses the .NET SDK image (~800MB). Restores and compiles.
#   Stage 2 "publish" → calls dotnet publish to produce the release binaries.
#   Stage 3 "final"   → uses the slim ASP.NET runtime image (~200MB). Copies
#                        only the published output. SDK is NOT in the final image.
#
#   Result: a ~200MB production image instead of ~800MB.
#   Security benefit: the final image has no compiler, no SDK, no source code.
#
# LEARNING — Build context:
#   This Dockerfile must be built from the REPO ROOT because it references
#   sibling projects (ProductCatalog.Data, SharedKernel) that live outside
#   this directory.
#
#   docker build -f backend/product-catalog/NexaCommerce.ProductCatalog/Dockerfile \
#                -t nexacommerce/product-catalog:latest \
#                .   ← the dot is the repo root as build context
#
#   In docker-compose.yml the 'context: .' and 'dockerfile:' keys do this automatically.
#
# LEARNING — Layer caching:
#   Docker caches each RUN/COPY instruction as a layer. Layers are reused if
#   their inputs haven't changed. The ORDER of instructions determines cache
#   effectiveness:
#
#   ✅ CORRECT order (maximises cache hits):
#     COPY *.csproj → RUN dotnet restore → COPY src → RUN dotnet build
#     The restore layer is only invalidated when a .csproj changes.
#     Source code changes don't force a restore.
#
#   ❌ WRONG order:
#     COPY . . → RUN dotnet restore
#     Any source change invalidates the restore layer → full restore every build.
#
# ═══════════════════════════════════════════════════════════════════════════════

# ── Stage 1: restore ────────────────────────────────────────────────────────
# LEARNING: Pin the exact SDK version matching global.json.
# Using 'latest' would break builds when Microsoft ships a new SDK with
# breaking changes. Pin to the same major.minor as global.json.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# LEARNING — Copy .csproj files first, then restore.
# Restoring before copying source code is the key layer-cache optimisation.
# If no .csproj changes, this layer is cached and restore is skipped.
# Copying the full Directory.Build.props and Directory.Packages.props
# is required because the csproj files implicitly import them.
COPY Directory.Build.props .
COPY Directory.Packages.props .
COPY NuGet.Config .

# Copy only the .csproj files for projects this service depends on.
# Each COPY creates its own cache layer — only the changed project's
# restore layer is invalidated.
COPY backend/shared-kernel/NexaCommerce.SharedKernel/NexaCommerce.SharedKernel.csproj \
     backend/shared-kernel/NexaCommerce.SharedKernel/

COPY backend/product-catalog/NexaCommerce.ProductCatalog.Data/NexaCommerce.ProductCatalog.Data.csproj \
     backend/product-catalog/NexaCommerce.ProductCatalog.Data/

COPY backend/product-catalog/NexaCommerce.ProductCatalog/NexaCommerce.ProductCatalog.csproj \
     backend/product-catalog/NexaCommerce.ProductCatalog/

# Restore NuGet packages for all three projects.
# --locked-mode: fail if packages.lock.json is out of sync with .csproj.
# This prevents "it worked on my machine" dependency drift in CI.
RUN dotnet restore backend/product-catalog/NexaCommerce.ProductCatalog/NexaCommerce.ProductCatalog.csproj \
    --locked-mode

# ── Stage 2: build + publish ────────────────────────────────────────────────
# Now copy the full source (cache miss only when source files change).
COPY backend/shared-kernel/NexaCommerce.SharedKernel/ backend/shared-kernel/NexaCommerce.SharedKernel/
COPY backend/product-catalog/NexaCommerce.ProductCatalog.Data/ backend/product-catalog/NexaCommerce.ProductCatalog.Data/
COPY backend/product-catalog/NexaCommerce.ProductCatalog/ backend/product-catalog/NexaCommerce.ProductCatalog/

# LEARNING — BUILD_CONFIGURATION build arg:
#   One Dockerfile handles Debug (Aspire dev) and Release (CI/production).
#   Pass --build-arg BUILD_CONFIGURATION=Debug for Aspire hot-reload scenarios.
#   CI always uses Release.
ARG BUILD_CONFIGURATION=Release

# LEARNING — dotnet publish vs dotnet build:
#   dotnet build      → compiles, outputs to bin/Debug|Release/
#   dotnet publish    → compiles + produces a self-contained deployment folder
#                       with all runtime dependencies. This is what goes in the image.
RUN dotnet publish backend/product-catalog/NexaCommerce.ProductCatalog/NexaCommerce.ProductCatalog.csproj \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    --no-restore

# ── Stage 3: final runtime image ─────────────────────────────────────────────
# LEARNING: aspnet:10.0 is the ASP.NET Core runtime — it has the CLR and
# ASP.NET Core libraries but NOT the SDK, compiler, or NuGet client.
# ~200MB vs ~800MB for the SDK image.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# LEARNING — Non-root user:
#   Running as root inside a container is a security risk — if the process is
#   compromised, the attacker has root access to the container filesystem.
#   ASP.NET Core images include the 'app' user for exactly this purpose.
USER app

# Copy ONLY the published output from the build stage.
# Nothing else from the SDK stage (no source, no NuGet cache, no SDK).
COPY --from=build /app/publish .

# LEARNING — ASPNETCORE_HTTP_PORTS:
#   The preferred way to set the HTTP port in .NET 8+.
#   Port 8080 is the standard non-root port for containerised ASP.NET Core.
#   (Port 80 requires root; 8080 does not.)
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "NexaCommerce.ProductCatalog.dll"]
