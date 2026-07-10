# GamaEdtech Backend (Gamatrain)

GamaEdtech Backend is the ASP.NET Core REST API behind the Gamatrain education platform: a
crowdsourced school directory with reviews, a blog, curriculum/exam content, gamified points,
crypto (Solana) + Stripe payments, subscriptions, and support tickets.

This README is a short entry point. The full documentation set lives in [`docs/`](docs/) and is
kept up to date alongside the code — start with [`PROJECT_SNAPSHOT.md`](PROJECT_SNAPSHOT.md) for
the current state of the system, or jump straight to a topic below.

## Documentation map

| Topic | Where |
|---|---|
| Current system snapshot (start here) | [`PROJECT_SNAPSHOT.md`](PROJECT_SNAPSHOT.md) |
| Architecture, layering, design patterns | [`docs/architecture/`](docs/architecture/) |
| Business domains & workflows | [`docs/business/`](docs/business/) |
| Database schema, entities, migrations | [`docs/database/`](docs/database/) |
| API endpoints, auth, response envelope | [`docs/api/`](docs/api/) |
| Local dev setup, coding standards, testing | [`docs/development/`](docs/development/) |
| CI/CD, deployment targets, configuration | [`docs/deployment/`](docs/deployment/) |
| How to contribute (commits, branches, PRs) | [`CONTRIBUTING.md`](CONTRIBUTING.md) |
| Instructions for AI coding agents | [`CLAUDE.md`](CLAUDE.md) |

## Technology stack (verified, `src/Directory.Packages.props`)

- **Runtime:** .NET 10 (`net10.0`), C# 14, `TreatWarningsAsErrors` + full analyzer set (SonarAnalyzer,
  VS Threading Analyzers), Central Package Management.
- **Web:** ASP.NET Core, `Asp.Versioning` (URL-segment versioning `api/v{version}`), Swashbuckle 9
  (Swagger UI at `/swagger`), output caching middleware (registered, not yet used by any endpoint),
  health checks (`/health`) + Hangfire dashboard (`/hangfire`).
- **Data:** EF Core 10 + SQL Server, NetTopologySuite (geospatial school search),
  `EntityFramework.Exceptions` (typed constraint-violation exceptions). ~107 migrations.
- **Auth:** ASP.NET Core Identity (cookie scheme) **plus** a custom opaque bearer-token scheme and
  an API-key scheme — see [`docs/api/authentication.md`](docs/api/authentication.md). There is
  **no JWT**.
- **Background jobs:** Hangfire (SQL Server storage) — recurring jobs for school scoring, reaction
  counters, sitemap generation, and more; see
  [`docs/architecture/cross-cutting-concerns.md`](docs/architecture/cross-cutting-concerns.md).
- **Caching:** Redis (`StackExchangeRedisCache`).
- **Logging:** Serilog.
- **External providers:** Resend (email), Google reCAPTCHA, Google OAuth, Stripe + a custom
  "GamaTrain" Solana payment gateway, Local/Azure/S3 file storage, YouTube.
- **Tests:** xUnit — currently integration-style, requiring a live SQL Server; not run in CI yet
  (see [`docs/development/testing.md`](docs/development/testing.md)).

## Getting started

Full instructions (prerequisites, connection strings, migrations, URLs once running):
[`docs/development/setup.md`](docs/development/setup.md). Quick version:

```bash
cd src
dotnet restore
dotnet build
dotnet run --project Presentation/Api --launch-profile GamaEdtech
```

The app applies pending EF Core migrations automatically at startup against whatever connection
string is configured — no separate `update-database` step is required. Once running:
Swagger UI at `https://localhost:7001/swagger`, health check at `https://localhost:7001/health`.

## Project structure

```
src/
├── Core/           # in-house framework (Common), DTOs (Data), localization resources (Resource)
├── Domain/         # EF entities, smart enumerations, specifications
├── Application/    # service interfaces + business-logic implementations
├── Infrastructure/ # EF DbContext, migrations, provider implementations (email/file/payment/...)
├── Presentation/   # view models + the ASP.NET Core API host (Controllers, Areas/Admin, Areas/Finance)
└── Test/           # xUnit tests
```

Full breakdown, request-flow diagram, and the dependency graph:
[`docs/architecture/overview.md`](docs/architecture/overview.md).

## Contributing

See [`CONTRIBUTING.md`](CONTRIBUTING.md) for commit message and branch naming conventions, and
[`docs/development/coding-standards.md`](docs/development/coding-standards.md) for the
entity → specification → DTO → service → view model → controller pattern every new feature
follows.

## Security

See [`SECURITY.md`](SECURITY.md) for how to report a vulnerability.

## License

No license file is currently present in this repository; treat the code as proprietary to
GamaEdtech unless told otherwise.
