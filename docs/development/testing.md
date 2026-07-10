# Testing

## Current state

The `src/Test` project (`GamaEdtech.Test.csproj`) contains 2 test classes across ~174 lines total:

- `src/Test/Application/IdentityServiceUnitTest.cs` — class `IdentityControllerUnitTest` (136 lines), 6 test methods covering registration: success, empty email/password, password-too-short, duplicate email.
- `src/Test/Infrastructure/Core/CoreProviderUnitTest.cs` — 1 test method (`ValidateTestAsync`) calling `ICoreProvider`.
- `src/Test/TestBase.cs` — shared base class.

These are **integration-style tests that require a live SQL Server database**, not isolated unit tests, despite the "UnitTest" naming:

- `TestBase.Services` (`src/Test/TestBase.cs:7`) is `Startup.Services`, a static `Lazy<IServiceProvider>` that builds a **real, second application host** via `Common.Hosting.Host.CreateHost<Startup>([])` (`src/Presentation/Api/Startup.cs:48`, `src/Core/Common/Hosting/Host.cs:35-63`).
- Test classes resolve real services from that host (`Services.Value!.GetRequiredService<...>()`, e.g. `IdentityServiceUnitTest.cs:24-25`) and call real controller/service methods, which go through the real `ApplicationDBContext` against whatever SQL Server the test process's `appsettings.json`/`appsettings.Development.json` points to. `GamaEdtech.Test.csproj` copies `Presentation/Api/appsettings.json` as its own config (`src/Test/GamaEdtech.Test.csproj:4-6`), so a test run needs a reachable SQL Server (and Redis, since the same `Startup` wires up `AddStackExchangeRedisCache`) exactly like running the API itself.
- Tests are **not repeatable**: `RegisterDuplicateUsernameShouldFail` (`IdentityServiceUnitTest.cs:107-134`) permanently registers `duplicate@example.com` on first run; a second run of `RegisterNormalUserShouldSucceed` (registers `testuser@example.com`) will fail on re-run because the user already exists from the prior run. There is no teardown/reset between runs.

## Running tests locally

Prerequisites: a running SQL Server reachable via the connection string in `appsettings.json`/`appsettings.Development.json` (same as for running the API — see `docs/development/setup.md`), and Redis reachable for the same reason.

```bash
cd src
dotnet test
```

Because of the non-repeatable duplicate-email test, a second consecutive `dotnet test` run against the same database may fail on `RegisterNormalUserShouldSucceed`/`RegisterDuplicateUsernameShouldFail` even with no code changes — this is a property of the current tests, not a regression.

## Known gap

None of the three deployment workflows (`main_gamaedtechv2.yml`, `staging.yml`, `vps-deploy-dotnet.yml`) run `dotnet test` — there is no automated test gate before merge or deploy today (see `docs/deployment/ci-cd.md`). Code can reach `main`/`staging` without the test project having been executed at all. Test coverage is also very small relative to the codebase (2 test classes covering registration and one core-provider call; no coverage of payments, transactions, schools, or most other services).
