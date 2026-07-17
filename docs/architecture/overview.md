# Architecture Overview

GamaEdtech Backend is a layered ASP.NET Core Web API (.NET 10, C# 14) serving the Gamatrain education
platform: school directory, blog, exams, tickets, gamification points, crypto (Solana) + Stripe payments,
and subscriptions.

This document covers solution/project structure and the request lifecycle. See also:
- `docs/architecture/design-patterns.md` — patterns you must follow when adding a feature.
- `docs/architecture/cross-cutting-concerns.md` — auth, jobs, caching, logging, versioning, health.

## Solution structure

All projects live under `src/` and target `net10.0` with settings shared from `src/Directory.Build.props`
(`TreatWarningsAsErrors`, `AnalysisMode=AllEnabledByDefault`, SonarAnalyzer, VS Threading analyzers,
central package management via `src/Directory.Packages.props`, `Nullable` enabled).

```
src/
├── GamaEdtech.sln
├── Directory.Build.props            # net10.0, LangVersion 14, analyzers, warnings-as-errors
├── Directory.Packages.props         # central NuGet package versions (~79 packages)
├── Build/Build.csproj                # meta-project: forces Resource+Service+Infrastructure to build/copy together
├── Core/
│   ├── Common/GamaEdtech.Common.csproj       # in-house framework (see design-patterns.md)
│   ├── Data/GamaEdtech.Data.csproj            # DTOs (Core/Data/Dto/<Feature>/...)
│   └── Resource/GamaEdtech.Resource.csproj    # .resx localization resources + generated Designer.cs
├── Domain/GamaEdtech.Domain.csproj
│   ├── Entity/            # EF entities (40+ files) + Identity entities (ApplicationUser, ApplicationRole)
│   ├── Enumeration/       # smart-enum classes (Domain.Enumeration.*, NOT native C# enum)
│   └── Specification/     # composable ISpecification<T> per aggregate (School/, Payment/, Identity/, ...)
├── Application/
│   ├── Interface/GamaEdtech.Application.Interface.csproj   # I*Service contracts (29 services)
│   └── Service/GamaEdtech.Application.Service.csproj       # implementations (SchoolService ~2076 lines, IdentityService ~1937 lines)
├── Infrastructure/
│   ├── Interface/GamaEdtech.Infrastructure.Interface.csproj   # provider contracts (file, email, captcha, payment gateway, currency converter...)
│   └── Infrastructure/GamaEdtech.Infrastructure.csproj
│       ├── EntityFramework/Context/ApplicationDBContext.cs   # the single EF DbContext (implements IEntityContext)
│       ├── Migrations/                                       # 215 files = 107 migrations + Designer counterparts + 1 ModelSnapshot
│       └── Provider/<Kind>/                                   # Email, File, Captcha, PaymentGateway, CurrencyConverter, Authentication, Core
├── Presentation/
│   ├── ViewModel/GamaEdtech.Presentation.ViewModel.csproj    # request/response view models per feature
│   └── Api/GamaEdtech.Presentation.Api.csproj (Sdk.Web)
│       ├── Startup.cs, Program.cs
│       ├── Controllers/                # public controllers
│       └── Areas/Admin/Controllers/, Areas/Finance/Controllers/   # admin- and finance-scoped controllers
└── Test/GamaEdtech.Test.csproj          # xUnit; 5 files / ~250 lines total (near-zero real coverage)
```

### Project reference graph (compile-time dependency direction)

```
Core/Common  ←  Domain  ←  Core/Data  ←  Infrastructure/Interface  ←  Infrastructure/Infrastructure
     ↑              ↑                          ↑                              ↑
     └────── Application/Interface ─────────────┴──────── Application/Service ─┘
                     ↑                                            ↑
           Presentation/ViewModel                                 │
                     ↑                                            │
              Presentation/Api  ───────────────────────────────────
                     ↑
                   Test
```

Concretely, from the `.csproj` `ProjectReference`s:
- `Domain` → `Core/Common` only.
- `Core/Data` → `Domain`, `Core/Common`.
- `Application/Interface` → `Core/Common`, `Core/Data` (defines contracts only, no Domain entities beyond what DTOs need).
- `Infrastructure/Interface` → `Core/Common`, `Core/Data`, `Domain`.
- `Application/Service` → `Core/Data`, `Infrastructure/Interface`, `Application/Interface` (service impls depend on provider *interfaces*, never concrete providers).
- `Infrastructure/Infrastructure` → `Core/Common`, `Core/Data`, `Domain`, `Infrastructure/Interface` (the only project that references EF Core provider packages + concrete SDKs: Azure.Storage.Blobs, AWSSDK.S3, Stripe.net, Resend, Google.Apis.YouTube.v3, PuppeteerSharp).
- `Presentation/ViewModel` → `Core/Common`, `Domain`.
- `Presentation/Api` → `Application/Interface`, `Presentation/ViewModel`, and `Build` (a non-`Private` reference that forces `Application/Service` + `Infrastructure/Infrastructure` + `Core/Resource` to be built and copied to the API's output, without the API project depending on their *types* directly — DI wiring/reflection resolves the concrete implementations at runtime).
- `Test` → `Core/Data`, `Application/Interface`, `Presentation/Api` (tests spin up the real `Startup`/host — see `docs/architecture/design-patterns.md` for why this is risky).

### What each layer is responsible for

| Layer | Responsibility |
|---|---|
| `Core/Common` | In-house framework: generic `Startup<TUser,TRole>` base, attribute-driven DI registration, specification base classes, `UnitOfWork`/`IRepository<T>`, `ResultData<T>`/`ApiResponse<T>`, smart-enum base class + model binders, data annotations, Serilog/Hangfire/health-check wiring, `IGenericFactory<TProvider,TEnum>`. |
| `Core/Data` | Feature DTOs (`GamaEdtech.Data.Dto.<Feature>`) used to move data between `Application/Service` and controllers without exposing EF entities. |
| `Core/Resource` | `.resx` resource files (validation messages, service error strings, enum display names) + generated designer classes. |
| `Domain/Entity` | EF Core entities, including ASP.NET Core Identity entities (`ApplicationUser`, `ApplicationRole`). |
| `Domain/Enumeration` | Smart enumerations (`Enumeration<TEnum,TKey>` subclasses) used instead of native C# `enum` everywhere business rules or display metadata attach to a value. |
| `Domain/Specification` | One `ISpecification<TEntity>` class per filter, organized by aggregate folder (`School/`, `Payment/`, `Identity/`, ...). |
| `Application/Interface` | `I<Feature>Service` contracts — the only thing controllers and other services depend on. |
| `Application/Service` | Business logic implementations; one class per feature, extends `LocalizableServiceBase<T>` (or `ServiceBase<T>`), talks to `IUnitOfWorkProvider` + provider interfaces, returns `ResultData<T>`. |
| `Infrastructure/Interface` | Contracts for external integrations (`IFileProvider`, `IEmailProvider`, `ICaptchaProvider`, `IPaymentGatewayProvider`, `ICurrencyConverterProvider`, `IMathFormulaRenderProvider`, ...) plus `IEntityContext`. |
| `Infrastructure/Infrastructure` | EF `ApplicationDBContext` (`src/Infrastructure/Infrastructure/EntityFramework/Context/ApplicationDBContext.cs`), 215 migration files, and concrete provider implementations grouped by kind under `Provider/`. |
| `Presentation/ViewModel` | Request/response view models with `GamaEdtech.Common.DataAnnotation` validation attributes (e.g. `[Display]`), one folder per feature. |
| `Presentation/Api` | ASP.NET Core host: `Startup.cs`, `Program.cs`, public `Controllers/`, and `Areas/Admin` + `Areas/Finance` controllers. |
| `Test` | xUnit project; currently 5 files (`TestBase.cs`, `IdentityServiceUnitTest.cs`, `CoreProviderUnitTest.cs`, `LocationsControllerUnitTest.cs`, `Usings.cs`) that build a real host (`Startup.Services`) against a live SQL Server — effectively integration tests, not unit tests, and not run in CI. |

## Request flow

```
 HTTP request
      │
      ▼
┌─────────────────────────────────────────────────────────────┐
│ Controller (Presentation/Api)                                │
│  route: api/v{version:apiVersion}/[controller]                │
│  or api/v{version:apiVersion}/[area]/[controller] for admin   │
│  - [Permission(...)] / [AllowAnonymous] on the action          │
│  - builds ISpecification<TEntity> from query/view-model params│
└─────────────────────────────────────────────────────────────┘
      │  calls  I<Feature>Service.SomeAsync(ListRequestDto<TEntity>{ Specification, PagingDto })
      ▼
┌─────────────────────────────────────────────────────────────┐
│ Application/Service (e.g. SchoolService.GetSchoolsListAsync)  │
│  - Lazy<IUnitOfWorkProvider>.Value.CreateUnitOfWork()          │
│  - uow.GetRepository<TEntity>().GetManyQueryable(specification)│
│  - LINQ projection to an anonymous type / DTO (no tracked      │
│    entity returned to the controller)                          │
│  - paging: query.Skip(...).Take(...) / FilterListAsync         │
│  - wraps outcome in ResultData<T>{ OperationResult, Data,       │
│    Errors } — never throws to the caller                       │
└─────────────────────────────────────────────────────────────┘
      │  ResultData<TDto>
      ▼
┌─────────────────────────────────────────────────────────────┐
│ Controller: maps ResultData<TDto>.Data → ViewModel             │
│  return OkWithFilter<T>(new(result.Errors){ Data = ... })       │
│  (or Ok<T>, BadRequest<T>, InternalServerError<T>)              │
└─────────────────────────────────────────────────────────────┘
      │  ApiResponse<TViewModel>{ Data, Errors, Succeeded }
      ▼
 HTTP 200 response body (see cross-cutting-concerns.md: errors are
 also returned with HTTP 200 today — OperationResult is not mapped
 to an HTTP status code)
```

Concrete example: `GET api/v1/Schools` →
`src/Presentation/Api/Controllers/SchoolsController.cs:41` (`GetSchools`) composes
`CountryIdEqualsSpecification`/`StateIdEqualsSpecification`/`NameContainsSpecification`/... via `.And()`,
then calls `schoolService.Value.GetSchoolsListAsync` (`src/Application/Service/SchoolService.cs:124`), which
does `uow.GetRepository<School>().GetManyQueryable(requestDto?.Specification)` (line 129), projects to an
anonymous type (lines 131-149), pages with `Skip`/`Take` (lines 156-160), and returns
`ResultData<ListDataSource<SchoolInfoDto>>`. The controller then builds a `SchoolInfoResponseViewModel` per
item (lines 111-133) and returns `OkWithFilter<...>(new(result.Errors){ Data = ... })` (line 107).

### Key framework types

| Type | File | Purpose |
|---|---|---|
| `ApiControllerBase<TClass>` | `src/Core/Common/Core/ApiControllerBase.cs:13` | Base for all controllers; typed `Ok<T>`/`BadRequest<T>`/`InternalServerError<T>` helpers that wrap `ApiResponse<T>`. |
| `ResultData<T>` | `src/Core/Common/Data/ResultData.cs:7` | `{ T? Data, OperationResult, IEnumerable<Error>? Errors }` — universal service return type. |
| `OperationResult` | `src/Core/Common/Core/Constants.cs:70` | `enum` (`NotFound=0, Succeeded=1, Failed=2, Duplicate=3, NotValid=4`). |
| `ApiResponse<T>` | `src/Core/Common/Data/ApiResponse.cs:9` | `{ T? Data, bool Succeeded, IEnumerable<Error>? Errors }`, the HTTP response envelope. |
| `ISpecification<T>` / `SpecificationBase<T>` | `src/Core/Common/DataAccess/Specification/ISpecification{T}.cs`, `SpecificationBase{T}.cs` | Composable query predicate + optional `Order`/`PageFilter`. |
| `IUnitOfWorkProvider` / `UnitOfWorkProvider` | `src/Core/Common/DataAccess/UnitOfWork/UnitOfWorkProvider.cs:14` | Wraps the scoped `DbContext` (`IEntityContext`) in an `IUnitOfWork`; **note**: every `CreateUnitOfWork()` call in one request shares the same scoped `DbContext` instance (see design-patterns.md pitfalls). |
| `IRepository<TEntity,TKey>` | `src/Core/Common/DataAccess/Repositories/IRepository.cs:14` | Generic query/command surface (`GetManyQueryable`, `Get`, `Query`, `Add`, `Update`, `Remove`, `Count`, `Any`, ...) parameterized by `ISpecification<T>` or raw predicates. |
| `IActionResult<T>` | `src/Core/Common/Data/IActionResult.cs:5` | Marker interface (extends `IActionResult`) so Swagger/`Produces<ApiResponse<T>>()` can describe the typed response shape of controller actions. |

## Auto-migration and configuration loading

`src/Core/Common/Hosting/Host.cs` builds configuration from `appsettings.json` +
`appsettings.{ASPNETCORE_ENVIRONMENT}.json` + environment variables (lines 38-43), configures Serilog
directly from that configuration (`Log.Logger = new LoggerConfiguration().ReadFrom.Configuration(config)...`,
lines 46-51), and — when run via `Host.RunAsync<TStartup, TUser, TRole>` (used by `Program.cs`) — calls
`context.Database.MigrateAsync()` against `IEntityContext` on every process start (lines 76-83). There is no
separate migration-deploy step in the CI workflows; the running application applies pending EF migrations
itself at boot.
