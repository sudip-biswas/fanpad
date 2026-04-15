# ── Build stage ───────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Copy solution and project files first (layer caching for restore)
COPY FanPad.ServiceMonitor.sln .
COPY src/FanPad.ServiceMonitor.Core/FanPad.ServiceMonitor.Core.csproj             src/FanPad.ServiceMonitor.Core/
COPY src/FanPad.ServiceMonitor.Infrastructure/FanPad.ServiceMonitor.Infrastructure.csproj  src/FanPad.ServiceMonitor.Infrastructure/
COPY src/FanPad.ServiceMonitor.Api/FanPad.ServiceMonitor.Api.csproj               src/FanPad.ServiceMonitor.Api/
COPY tests/FanPad.ServiceMonitor.Tests/FanPad.ServiceMonitor.Tests.csproj         tests/FanPad.ServiceMonitor.Tests/

RUN dotnet restore

# Copy all source and build
COPY . .
RUN dotnet publish src/FanPad.ServiceMonitor.Api/FanPad.ServiceMonitor.Api.csproj \
    -c Release -o /publish --no-restore

# ── Runtime stage ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Non-root user for security
RUN addgroup --system fanpad && adduser --system --ingroup fanpad fanpad
USER fanpad

COPY --from=build /publish .

EXPOSE 5000
ENV ASPNETCORE_URLS=http://+:5000
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "FanPad.ServiceMonitor.Api.dll"]
