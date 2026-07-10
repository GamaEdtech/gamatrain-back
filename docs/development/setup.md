# Local Development Setup

## Prerequisites

- **.NET 10 SDK** — the solution targets `net10.0` / C# 14 (`src/Directory.Build.props:12-13`). The root `README.md` still says .NET 9; that is out of date, follow this document instead.
- **A reachable SQL Server instance** (SQL Server 2022+ recommended). The API connects via `Connection:ConnectionString` in `src/Presentation/Api/appsettings.json:2-8` (default: `Server=.;Database=GamaEdtech;Trusted_Connection=True;TrustServerCertificate=True`). Any reachable instance works — local install, remote box, or a user-mode instance — the app does not care how SQL Server is hosted.
- **Redis** — configured under `Cache:InstanceName` / `Cache:Configuration` (`appsettings.json:140-143`) and wired up unconditionally in `src/Presentation/Api/Startup.cs:59-63` (`AddStackExchangeRedisCache`) and as a health check dependency (`Startup.cs:184-188`, `AddRedis(...)`). `StackExchangeRedisCache` connects lazily, so the process itself can start without Redis reachable, but:
  - `/health` and `/healthz` will report the Redis check as unhealthy, and
  - any request path that touches the distributed cache/output cache will throw.
  Treat Redis as required for a fully working local instance, optional only if you just need the process to boot.
- Hangfire (background jobs) uses SQL Server storage, not Redis (`Startup.cs:53-57`), so it only needs the SQL Server connection above.

There is no Docker Compose file in this repository (checked repo root and `src/`) — local dependencies (SQL Server, Redis) must be provided by some other means; there is no containerized dev-stack story currently.

## Restore, build, run

From the `src/` directory (this is the solution root — `src/GamaEdtech.sln`):

```bash
dotnet restore
dotnet build
```

`Directory.Build.props` enables `TreatWarningsAsErrors`, `AnalysisMode=AllEnabledByDefault`, and SonarAnalyzer/StyleCop/VS-Threading analyzers solution-wide (`src/Directory.Build.props:17-20,33-39`), so a clean `dotnet build` is the actual quality gate — any new warning fails the build locally, exactly as it would (if it ran) in CI.

Run the API:

```bash
dotnet run --project Presentation/Api
```

or, to use the checked-in `GamaEdtech` launch profile (sets `ASPNETCORE_ENVIRONMENT=Development`, `src/Presentation/Api/Properties/launchSettings.json`):

```bash
dotnet run --project Presentation/Api --launch-profile GamaEdtech
```

## Migrations

The DbContext is `GamaEdtech.Infrastructure.EntityFramework.Context.ApplicationDBContext` (`src/Infrastructure/Infrastructure/EntityFramework/Context/ApplicationDBContext.cs:17-18`). Migrations live in `src/Infrastructure/Infrastructure/Migrations/` (215+ files).

**Migrations are applied automatically on startup** — `Program.cs` calls `Common.Hosting.Host.RunAsync<Startup, ApplicationUser, ApplicationRole>(args)`, and `Host.RunInternalAsync` runs `await context.Database.MigrateAsync()` before starting the host whenever a user/role-aware `Startup` is used (`src/Core/Common/Hosting/Host.cs:76-83`). In practice this means: point the connection string at an empty (or behind-schema) database and simply run the app — pending migrations are applied before the server starts listening.

To add a new migration or apply migrations manually without running the full app, use the standard EF Core CLI, pointing the migrations project at Infrastructure and the startup project at the API:

```bash
dotnet ef migrations add <Name> \
  --project Infrastructure/Infrastructure/GamaEdtech.Infrastructure.csproj \
  --startup-project Presentation/Api/GamaEdtech.Presentation.Api.csproj

dotnet ef database update \
  --project Infrastructure/Infrastructure/GamaEdtech.Infrastructure.csproj \
  --startup-project Presentation/Api/GamaEdtech.Presentation.Api.csproj
```

(No `IDesignTimeDbContextFactory` was found in the repo, so `dotnet-ef` resolves the context through the API's own host/DI at design time — the `--startup-project` above must be able to build and its configuration must point at a reachable SQL Server instance.)

## Connection strings / local overrides

Base configuration lives in `src/Presentation/Api/appsettings.json` (tracked in git — see the secrets callout in `docs/deployment/configuration.md`). For local overrides, create/edit `src/Presentation/Api/appsettings.Development.json` — it is listed in `.gitignore` (`/src/Presentation/Api/appsettings.Development.json`) so local values (connection string, Redis endpoint, API keys) never get committed. It is loaded automatically when `ASPNETCORE_ENVIRONMENT=Development` (`src/Core/Common/Hosting/Host.cs:38-43`), which the `GamaEdtech` launch profile sets for you.

Minimal example `appsettings.Development.json`:

```json
{
  "Connection": {
    "ConnectionString": "Server=localhost;Database=GamaEdtech;Trusted_Connection=True;TrustServerCertificate=True"
  },
  "Cache": {
    "Configuration": "localhost:6379,abortConnect=False"
  }
}
```

## URLs once running

With the `GamaEdtech` launch profile the app listens on `https://localhost:7001` and `http://localhost:7000` (`src/Presentation/Api/Properties/launchSettings.json`):

- Swagger UI: `https://localhost:7001/swagger`
- Health check: `https://localhost:7001/health`
- Health check UI (detailed, requires authorization): `https://localhost:7001/health/details`
- Hangfire dashboard: `https://localhost:7001/hangfire`

## Example local workflow (this style of dev box)

One way to satisfy the prerequisites above without Docker/root access: run SQL Server, Redis, and (if you need external inbound access, e.g. OAuth callbacks) a Cloudflare tunnel in user-mode via a helper script (e.g. `~/start-dev-stack.sh`), then run the API from `src/`:

```bash
~/start-dev-stack.sh          # starts local SQL Server / Redis / tunnel processes
cd src
dotnet run --project Presentation/Api --launch-profile GamaEdtech
```

This is one example, not a repo requirement — any machine with the .NET 10 SDK and a reachable SQL Server (and, ideally, Redis) instance works the same way.
