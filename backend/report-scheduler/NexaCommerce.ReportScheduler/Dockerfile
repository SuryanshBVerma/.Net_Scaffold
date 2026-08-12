# ═══════════════════════════════════════════════════════════════════════════════
# NexaCommerce.ReportScheduler — Multi-Stage Dockerfile
# ═══════════════════════════════════════════════════════════════════════════════
#
# LEARNING — Worker vs Web service Dockerfile differences:
#
#   ProductCatalog (web):     uses aspnet:10.0 runtime image + EXPOSE 8080
#   ReportScheduler (worker): uses runtime:10.0 (no ASP.NET Core) + no EXPOSE
#
#   The 'dotnet/runtime' image is smaller than 'dotnet/aspnet' because it
#   doesn't include the ASP.NET Core libraries. Workers don't need HTTP.
#
#   Rule: use dotnet/aspnet for web hosts; dotnet/runtime for workers.
#
# Build from repo root:
#   docker build -f backend/report-scheduler/NexaCommerce.ReportScheduler/Dockerfile \
#                -t nexacommerce/report-scheduler:latest \
#                .
# ═══════════════════════════════════════════════════════════════════════════════

# ── Stage 1: restore ────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props .
COPY Directory.Packages.props .
COPY NuGet.Config .

# Copy .csproj files first for maximum layer cache utilisation.
COPY backend/shared-kernel/NexaCommerce.SharedKernel/NexaCommerce.SharedKernel.csproj \
     backend/shared-kernel/NexaCommerce.SharedKernel/

COPY backend/product-catalog/NexaCommerce.ProductCatalog.Data/NexaCommerce.ProductCatalog.Data.csproj \
     backend/product-catalog/NexaCommerce.ProductCatalog.Data/

COPY backend/report-scheduler/NexaCommerce.ReportScheduler.Data/NexaCommerce.ReportScheduler.Data.csproj \
     backend/report-scheduler/NexaCommerce.ReportScheduler.Data/

COPY backend/report-scheduler/NexaCommerce.ReportScheduler/NexaCommerce.ReportScheduler.csproj \
     backend/report-scheduler/NexaCommerce.ReportScheduler/

RUN dotnet restore backend/report-scheduler/NexaCommerce.ReportScheduler/NexaCommerce.ReportScheduler.csproj \
     --force-evaluate

# ── Stage 2: build + publish ────────────────────────────────────────────────
COPY backend/shared-kernel/NexaCommerce.SharedKernel/ backend/shared-kernel/NexaCommerce.SharedKernel/
COPY backend/product-catalog/NexaCommerce.ProductCatalog.Data/ backend/product-catalog/NexaCommerce.ProductCatalog.Data/
COPY backend/report-scheduler/NexaCommerce.ReportScheduler.Data/ backend/report-scheduler/NexaCommerce.ReportScheduler.Data/
COPY backend/report-scheduler/NexaCommerce.ReportScheduler/ backend/report-scheduler/NexaCommerce.ReportScheduler/

ARG BUILD_CONFIGURATION=Release

RUN dotnet publish backend/report-scheduler/NexaCommerce.ReportScheduler/NexaCommerce.ReportScheduler.csproj \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    --no-restore

# ── Stage 3: final runtime image ─────────────────────────────────────────────
# LEARNING: dotnet/runtime (not dotnet/aspnet) — workers don't need ASP.NET Core.
# This shaves ~50MB from the final image.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

USER app

COPY --from=build /app/publish .

# No EXPOSE — this is a worker with no HTTP surface.
# It communicates exclusively through RabbitMQ and PostgreSQL.

ENTRYPOINT ["dotnet", "NexaCommerce.ReportScheduler.dll"]
