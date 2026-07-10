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

## Known issue: the test suite currently does not pass as documented (found 2026-07-10)

While adding tests for the subscription feature, two problems surfaced that make `dotnet test`
unreliable in its *current* form — worth knowing before spending time debugging a "why does my new
test fail" mystery that turns out to be pre-existing:

1. **`ApplicationDBContext` is registered `[ServiceLifetime(ServiceLifetime.Transient, ...)]`**
   (`src/Infrastructure/Infrastructure/EntityFramework/Context/ApplicationDBContext.cs:16`), and its
   `OnConfiguring` branches on `Environment.GetEnvironmentVariable("Test") == "True"` (set
   unconditionally by `TestBase`'s constructor) to call
   `optionsBuilder.UseInMemoryDatabase(Guid.NewGuid().ToString(), ...)` — **a fresh, randomly-named
   in-memory database on every single resolution**, not the live SQL Server this document otherwise
   describes. Because `UnitOfWorkProvider.CreateUnitOfWork()` resolves a new `IEntityContext` from the
   scope on every call, **every separate service call within one test gets its own empty in-memory
   database** — nothing written by one call is visible to a later call in the same test. Any test
   whose assertion depends on cross-call state (e.g. create-then-read-back, or two calls checking a
   duplicate) cannot pass as a result — this is not something a test author can work around by
   creating an explicit DI scope; the context itself is Transient regardless of scope.
2. **Resolving a `Lazy<T>`-wrapped scoped dependency directly from the root `IServiceProvider`
   (exactly what every existing test does, e.g. `Services.Value!.GetRequiredService<Lazy<IIdentityService>>()`)
   throws `InvalidOperationException: Cannot resolve '...' from root provider because it requires
   scoped service 'IUnitOfWorkProvider'`** whenever `ASPNETCORE_ENVIRONMENT=Development` is set (this
   environment enables .NET's strict DI scope validation by default). The tests still don't need this
   env var for the DB connection string, per point 1 — but the environment matters for this reason
   instead, and is easy to reach for by accident when a connection error looks like it needs an
   explicit `Development` environment.

**Verified concretely**: running the documented `cd src && dotnet test` command exactly as written,
with no other changes, currently fails `IdentityControllerUnitTest.RegisterNormalUserShouldSucceed`
(a pre-existing, unmodified test) — so this is not new breakage from any particular feature branch,
it reflects the test project's current baseline state. Treat any `dotnet test` "pass" you see as
suspect until this is fixed; a red run is the current normal, not a regression signal.

This is why the subscription-quota feature (`docs/business/subscriptions.md`) does not ship with
`dotnet test` coverage for its purchase/activate/consume flow — an attempt was made
(create-plan-then-purchase-then-activate, exactly the kind of cross-call flow described above) and
it failed for the reasons above, not because of a defect in the feature. That flow was instead
verified manually against the real dev SQL Server via direct API calls (admin CRUD, purchase
creation, pending-vs-active `GET me` distinction, quota-then-points fallback and upgrade-suggestion
surfacing on `games/spends`).
