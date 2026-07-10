# Coding Standards & Conventions

This document describes the conventions an existing contributor already follows in this codebase. Mirror them when adding a feature — the layering is consistent across ~30 features, and deviating from it is more expensive than following it.

## Build hygiene (enforced, not optional)

- **`TreatWarningsAsErrors=true`**, `AnalysisMode=AllEnabledByDefault`, `AnalysisLevel=latest-all`, plus SonarAnalyzer/StyleCop/VS-Threading analyzers — all set solution-wide in `src/Directory.Build.props:17-20,33-39`. A warning anywhere fails `dotnet build`. Fix analyzer warnings; do not suppress them ad hoc.
- **Central Package Management** — all package versions are declared once in `src/Directory.Packages.props`. Individual `.csproj` files must use bare `<PackageReference Include="..."/>` with **no `Version` attribute** (see any existing `.csproj`, e.g. `src/Presentation/Api/GamaEdtech.Presentation.Api.csproj:19-38`). Adding a new package: add the version to `Directory.Packages.props`, then reference it without a version in the consuming project.
- `Nullable` and `ImplicitUsings` are enabled solution-wide (`Directory.Build.props:15-16`).

## Feature layering (entity → controller)

1. **Entity** — `src/Domain/Entity/` (+ EF configuration). Add a migration in `src/Infrastructure/Infrastructure/Migrations/` against `ApplicationDBContext` (`src/Infrastructure/Infrastructure/EntityFramework/Context/ApplicationDBContext.cs`).
2. **Specifications** — `src/Domain/Specification/<Feature>/`, one small composable class per filter, e.g. `CountryIdEqualsSpecification`, `StateIdEqualsSpecification` (used in `src/Presentation/Api/Controllers/SchoolsController.cs:41-53`). Compose with `.And(...)` rather than writing ad-hoc LINQ predicates in the service or controller.
3. **DTOs** — `src/Core/Data/Dto/<Feature>/` (internal service ↔ repository shape).
4. **Service contract + implementation**:
   - Interface in `src/Application/Interface/I<Feature>Service.cs`.
   - Implementation in `src/Application/Service/<Feature>Service.cs`, extending `LocalizableServiceBase<T>` (`src/Core/Common/Service/LocalizableServiceBase.cs:11`).
   - **All dependencies are injected as `Lazy<T>`** — constructor pattern example, `src/Application/Service/TagService.cs:26-28`:
     ```csharp
     public class TagService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor,
         Lazy<IStringLocalizer<TagService>> localizer, Lazy<ILogger<TagService>> logger)
         : LocalizableServiceBase<TagService>(unitOfWorkProvider, httpContextAccessor, localizer, logger), ITagService
     ```
   - Methods **return `ResultData<T>`** (`src/Core/Common/Data/ResultData.cs`) — never throw to the caller. `OperationResult` is one of `Succeeded`, `Failed`, `NotFound`, `NotValid`, `Duplicate`.
   - Data access goes through `UnitOfWorkProvider.Value.CreateUnitOfWork()` → `uow.GetRepository<TEntity>()` → a projected LINQ query (see `TagService.GetTagsAsync`, `src/Application/Service/TagService.cs:29-`). Prefer projecting to a DTO over loading tracked entities.
5. **ViewModels** — `src/Presentation/ViewModel/<Feature>/`, request/response shapes with DataAnnotations from `GamaEdtech.Common.DataAnnotation`.
6. **Controller** — `src/Presentation/Api/Controllers/` (public) or `src/Presentation/Api/Areas/Admin/Controllers/` (admin):
   - Extend `ApiControllerBase<TClass>` (`src/Core/Common/Core/ApiControllerBase.cs:13`).
   - Route: `[Route("api/v{version:apiVersion}/[controller]")]` + `[ApiVersion("1.0")]` (see `SchoolsController.cs:32-33`).
   - Permission: public controllers use `[Permission(policy: null)]` at the class level and `[AllowAnonymous]` per action where needed (`SchoolsController.cs:34,37`); admin controllers use `[Permission(Roles = [nameof(Role.Admin)])]` (e.g. `src/Presentation/Api/Areas/Admin/Controllers/BlogsController.cs:30`, `ApplicationSettingsController.cs:20`).
   - Wrap responses in `ApiResponse<T>`; map the DTO to the view model in the controller (not the service).
7. **Localization strings** — resource keys in `src/Core/Resource`, referenced via `Localizer.Value["Key"]`.
8. **Background work** — register a Hangfire recurring job in `Startup.ConfigureCore` (`src/Presentation/Api/Startup.cs`) rather than ad-hoc timers.
9. **External integrations** (payment, email, file storage, captcha, currency conversion) — define a provider interface in `src/Infrastructure/Interface/`, implement it in `src/Infrastructure/Infrastructure/Provider/<Kind>/`, and resolve it through `IGenericFactory<TProvider, TEnum>` (`src/Core/Common/Service/Factory/IGenericFactory.cs`) keyed by a smart enum, configured under a section in `appsettings.json`. Do not hardwire a concrete provider into a service.

## Error handling

The existing pattern in every service/controller method is a try/catch that logs and returns `ResultData` with `exc.Message` as the error text. **Do not add new endpoints that leak `exc.Message` to the client** — this is a known issue (raw exception text, including SQL error text, is returned to API consumers). If you can avoid it in new code (return a generic localized error message instead, still logging the real exception via `LogException`), do so; do not propagate the anti-pattern further than it already is. A shared exception-handling middleware is the tracked long-term fix, not something to hand-roll per endpoint.

## Naming — do not propagate existing typos

A few existing filenames/identifiers have typos that predate any style guide and are kept only for historical reasons:
- `src/Application/Service/ExamSerivce.cs` (class `ExamSerivce`)
- `src/Application/Service/GameSerivce.cs`

**Do not copy this typo (`Serivce`) into new service names.** New services should be named `<Feature>Service.cs` / `class <Feature>Service`, matching the majority pattern (`TagService`, `SchoolService`, `TransactionService`, etc.).

## Smart enumerations

Domain enums such as `Role` (`src/Domain/Enumeration/Role.cs`) are smart-enum classes, not plain C# `enum`s, and are bound via custom model binders. Follow the existing smart-enum pattern in `src/Domain/Enumeration/` rather than introducing a plain `enum` for a new domain concept that needs the same binding/localization behavior.

## What not to imitate from `README.md`

The root `README.md` lists Dapper, FluentValidation, JWT auth, and "Fluent Assertion" as part of the stack — none of these are used. Validation is DataAnnotations-based; auth is ASP.NET Core Identity (cookie scheme) plus a custom opaque-token scheme. Don't design new code around the README's stack description; follow the patterns actually present in `src/`.

## Commit messages and branches

Follow `CONTRIBUTING.md` as-is (Conventional Commits — `type(scope): subject`; branch names like `feat/123-short-description`). This document does not change or duplicate those rules.
