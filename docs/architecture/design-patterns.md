# Design Patterns & Conventions

These are the recurring patterns used throughout the codebase. New features **must** follow them —
reviewers will expect it, and the framework in `Core/Common` assumes it (e.g. DI registration only works
for types that follow the `[Injectable]`/`[ServiceLifetime]` convention below).

## 1. Specification pattern

Every query filter is a small class implementing `ISpecification<T>` (or extending `SpecificationBase<T>`),
composed with `.And()`/`.Or()`/`.Not()` instead of ad-hoc LINQ scattered through controllers/services.

- Interface: `src/Core/Common/DataAccess/Specification/ISpecification{T}.cs`
  ```csharp
  public interface ISpecification<T>
  {
      PageFilter? PageFilter { get; }
      Func<IQueryable<T>, IOrderedQueryable<T>>? Order { get; }
      Expression<Func<T, bool>> Expression();
      bool IsSatisfiedBy(T candidate);
  }
  ```
- Base class: `src/Core/Common/DataAccess/Specification/SpecificationBase{T}.cs` — implements `IsSatisfiedBy`
  by compiling `Expression()`; subclasses only implement `Expression()`.
- Composition operators: `src/Core/Common/DataAccess/Specification/SpecificationExtensions.cs`
  (`And`, `AndNot`, `Or`, `OrNot`, `Not`), backed by `AndSpecification{T}.cs`, `OrSpecification{T}.cs`, etc.
  in the same folder.
- Concrete example: `src/Domain/Specification/School/CountryIdEqualsSpecification.cs` and 12 siblings in
  `src/Domain/Specification/School/` (`StateIdEqualsSpecification`, `NameContainsSpecification`,
  `HasRatingSpecification`, `TuitionSpecification`, `LocationIncludeSpecification`, ...).
- Composition in a controller: `src/Presentation/Api/Controllers/SchoolsController.cs:45-98` builds up
  `baseSpecification` conditionally per query parameter:
  ```csharp
  ISpecification<School>? baseSpecification = null;
  if (request.CountryId.HasValue)
      baseSpecification = new CountryIdEqualsSpecification(request.CountryId.Value);
  if (request.StateId.HasValue)
  {
      var specification = new StateIdEqualsSpecification(request.StateId.Value);
      baseSpecification = baseSpecification is null ? specification : baseSpecification.And(specification);
  }
  ```
- Consumption: `IRepository<TEntity,TKey>.GetManyQueryable(ISpecification<TEntity>?, bool tracking = false)`
  (`src/Core/Common/DataAccess/Repositories/IRepository.cs:20`) turns the specification's `Expression()`
  into a `Where(...)` clause; see `src/Application/Service/SchoolService.cs:129`.

**Rule:** add one specification class per filterable field/condition, in
`Domain/Specification/<Feature>/<Field><Operator>Specification.cs`. Do not write inline `Where` lambdas in
services/controllers for anything reusable.

## 2. `ResultData<T>` / `OperationResult` — services never throw to callers

- `src/Core/Common/Data/ResultData.cs:7`: `struct ResultData<T>(OperationResult) { T? Data; OperationResult OperationResult; IEnumerable<Error>? Errors; }`.
- `OperationResult` enum (`src/Core/Common/Core/Constants.cs:70-77`): `NotFound = 0, Succeeded = 1, Failed = 2, Duplicate = 3, NotValid = 4`.
- Every public service method wraps its body in `try { ... return new(OperationResult.Succeeded){ Data = ... }; } catch (Exception exc) { Logger.Value.LogException(exc); return new(OperationResult.Failed){ Errors = [new(){ Message = exc.Message }] }; }`.
  Example: `src/Application/Service/TransactionService.cs:90-104` (`GetCurrentBalanceAsync`).
- Specific `OperationResult` values are returned deliberately for expected conditions, not just exceptions —
  e.g. `PaymentService.VerifyPaymentAsync` returns `OperationResult.NotFound` for "payment not found" *and*
  for "payment not in Pending status" (`src/Application/Service/PaymentService.cs:141-155`), and
  `TransactionService`'s inner `catch (UniqueConstraintException)` returns `OperationResult.Duplicate`
  (`src/Application/Service/TransactionService.cs:274-277`).
- Controllers translate `ResultData<T>` into `ApiResponse<T>` (`src/Core/Common/Data/ApiResponse.cs:9`) via
  `Ok<T>`/`OkWithFilter<T>` on `ApiControllerBase<TClass>` (`src/Core/Common/Core/ApiControllerBase.cs:34-47`).

**Known pitfall (do not copy without checking):** controllers frequently dereference `result.Data.List`
before checking `result.OperationResult`, which throws `NullReferenceException` when the service returned
`Failed`/`NotFound` (that exception is then swallowed by the controller's own try/catch and returned as an
opaque "Object reference not set..." message). See
`src/Presentation/Api/Controllers/SchoolsController.cs:109` for an instance of the pattern. Always check
`result.OperationResult == OperationResult.Succeeded` (or at least `result.Data is not null`) before use.

**Known pitfall #2:** `ApiControllerBase<TClass>.Ok<T>` and `OkWithFilter<T>` return HTTP 200 even when
`Errors` is non-empty — the HTTP status code does not reflect `OperationResult`. See
`docs/architecture/cross-cutting-concerns.md` for the implication.

## 3. `Lazy<T>` dependency injection

All constructor dependencies across services and controllers are declared as `Lazy<TDependency>` and
accessed via `.Value`, not injected directly.

- Registration: `services.AddTransient(typeof(Lazy<>))` in
  `src/Core/Common/Startup/Startup{TUser,TRole}.cs:211` — this is what makes `Lazy<T>` resolvable for
  *any* `T` the container knows about (the built-in .NET DI container does not support `Lazy<T>` out of
  the box; this line makes `IServiceProvider` construct a `Lazy<T>` whose factory calls back into the
  container to resolve `T`, giving effectively deferred/on-demand resolution).
- Example service constructor: `src/Application/Service/SchoolService.cs:44-46`
  ```csharp
  public class SchoolService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor,
      Lazy<IStringLocalizer<SchoolService>> localizer, Lazy<IEmailService> emailService, Lazy<ILogger<SchoolService>> logger,
      Lazy<IFileService> fileService, Lazy<IContributionService> contributionService, Lazy<IIdentityService> identityService,
      /* ... */)
      : LocalizableServiceBase<SchoolService>(unitOfWorkProvider, httpContextAccessor, localizer, logger), ISchoolService, ISiteMapHandler
  ```
- Base classes store the `Lazy<T>` itself (not `.Value`) as protected properties:
  `src/Core/Common/Service/ServiceBase.cs:13-17` (`Logger`, `HttpContextAccessor`, `UnitOfWorkProvider`) and
  `src/Core/Common/Service/LocalizableServiceBase.cs:15` (`Localizer`).
- Controllers follow the same convention: `src/Presentation/Api/Controllers/SchoolsController.cs:35-36`.

**Rule:** when adding a new dependency to a service or controller constructor, inject `Lazy<IYourDependency>`
and call `.Value` at the use site — this is consistent throughout the codebase (helps break circular
dependencies between services, e.g. `SchoolService` ⇄ `IdentityService`, since resolution is deferred).

## 4. Provider / factory pattern for swappable external integrations

External integrations (payment gateways, currency converters, file storage, email, captcha) are defined as
an interface implementing `IProvider<TEnum>` and resolved through a generic keyed factory rather than
injected directly, so the concrete implementation is chosen at runtime by a smart-enum value (often from
config).

- Factory contract: `src/Core/Common/Service/Factory/IGenericFactory.cs:6`
  ```csharp
  [DataAnnotation.Injectable]
  public interface IGenericFactory<TProvider, TProviderType>
      where TProvider : IProvider<TProviderType>
      where TProviderType : Enumeration<TProviderType, byte>
  {
      TProvider? GetProvider(TProviderType providerType, bool returnFirstItemIfNotMatch = false);
  }
  ```
- Implementation: `src/Core/Common/Service/Factory/GenericFactory.cs:9-25` — takes `IEnumerable<TProvider>`
  (all registered implementations get injected as a collection) and picks the one whose `ProviderType`
  matches.
- Concrete usage: `IGenericFactory<IPaymentGatewayProvider, PaymentGateway>` and
  `IGenericFactory<ICurrencyConverterProvider, Currency>`, injected into `PaymentService`
  (`src/Application/Service/PaymentService.cs:30-31`) and resolved per-payment:
  `gatewayFactory.Value.GetProvider(payment.Gateway)!.VerifyAsync(...)`
  (`src/Application/Service/PaymentService.cs:157`).
- Two concrete providers behind `IPaymentGatewayProvider`:
  `src/Infrastructure/Infrastructure/Provider/PaymentGateway/GamaTrainPaymentGatewayProvider.cs` (Solana) and
  `.../StripePaymentGatewayProvider.cs`, keyed by the smart enum `src/Domain/Enumeration/PaymentGateway.cs`.
- Other provider families follow the same shape: `Provider/Email/`, `Provider/File/` (Local/Azure/S3),
  `Provider/Captcha/`, `Provider/CurrencyConverter/`, `Provider/Authentication/` (Google OAuth),
  `Provider/ContentDelivery/` (currently one implementation, `GamaApiContentDeliveryProvider`, keyed
  by `ContentSource` — see `docs/business/content-delivery.md`), all under
  `src/Infrastructure/Infrastructure/Provider/`.

**Rule:** when adding a new external integration with multiple possible backends, define
`I<Kind>Provider : IProvider<TEnum>` in `Infrastructure/Interface`, add one implementation class per backend
in `Infrastructure/Infrastructure/Provider/<Kind>/`, add/extend a smart enum for the backend selector, and
resolve it through `IGenericFactory<I<Kind>Provider, TEnum>` — never `new` the provider directly or inject
a specific implementation.

## 5. Smart enumerations (`Domain.Enumeration`), not native C# `enum`

- Base class: `Enumeration<TEnum, TKey>` in `src/Core/Common/Data/Enumeration/Enumeration.cs:13`
  (`IComparable`, `IEquatable<Enumeration<TEnum,TKey>>`, `IRouteConstraint` — so it can be used directly in
  route templates/model binding).
- Concrete example: `src/Domain/Enumeration/TagType.cs`
  ```csharp
  public sealed class TagType : Enumeration<TagType, byte>
  {
      [Display] public static readonly TagType School = new(nameof(School), 0);
      [Display] public static readonly TagType Post = new(nameof(Post), 1);
      [Display] public static readonly TagType Feature = new(nameof(Feature), 2);
      public TagType() { }
      public TagType(string name, byte value) : base(name, value) { }
  }
  ```
- Custom ASP.NET Core model binders make these usable as query/route parameters like a normal enum:
  `src/Core/Common/ModelBinding/EnumerationQueryStringModelBinder.cs` +
  `EnumerationQueryStringModelBinderProvider.cs` (registered in
  `src/Core/Common/Startup/Startup{TUser,TRole}.cs:401`), plus a `Flags`-style variant
  (`FlagsEnumerationQueryStringModelBinderProvider`, line 402) for bitset-like enumerations.
- Swagger describes them via `src/Core/Common/Swagger/EnumerationToEnumSchemaFilter.cs` and
  `EnumerationParameterFilter.cs` so the generated OpenAPI spec still shows a normal enum-like schema despite
  the custom type.
- `[Display]` attributes on each value drive localized display names via the resource files in
  `Core/Resource`.

**Rule:** whenever a value has fixed known members with business meaning (payment gateway, tag type,
permission role, school type, ...), model it as an `Enumeration<TEnum, TKey>` subclass in
`Domain/Enumeration/`, not a native `enum`. Native enums lose the model-binder/Swagger/`IGenericFactory` key
support wired up for `Enumeration<,>`.

## 6. `Core/Common` in-house framework

`Core/Common` (`GamaEdtech.Common.csproj`) is a general-purpose framework this specific codebase is built
on. Its main pieces, each referenced elsewhere in this document:

| Piece | File(s) | Purpose |
|---|---|---|
| Attribute-scanning DI registration | `src/Core/Common/DataAnnotation/InjectableAttribute.cs`, `ServiceLifetimeAttribute.cs`, wiring in `src/Core/Common/Startup/Startup{TUser,TRole}.cs:408-451` (`AddScopedDynamic`) | Interfaces marked `[Injectable]` (assembly-level attribute added via each `.csproj`'s `<AssemblyAttribute Include="GamaEdtech.Common.DataAnnotation.InjectableAttribute">`) are matched at startup, by reflection over loaded assemblies, to implementing classes; the implementation's `[ServiceLifetime(...)]` attribute (default `Transient`) decides the DI lifetime. No manual `services.AddScoped<IFoo, Foo>()` calls are needed for services/providers/repositories that follow this convention. |
| Generic `Startup<TUser,TRole>` base | `src/Core/Common/Startup/Startup{TUser,TRole}.cs`, `StartupOption.cs` | The app's `Startup` (`src/Presentation/Api/Startup.cs:23-33`) only overrides `ConfigureServicesCore`/`ConfigureCore` and passes a `StartupOption` (Localization, Authentication, Https, Identity, ErrorCodePrefix); the base class does the heavy lifting (MVC options, auth schemes, identity, DI scanning, Swagger, model binders, culture). |
| Data annotations | `src/Core/Common/DataAnnotation/` (e.g. `[Display]`, `[Injectable]`, `[ServiceLifetime]`, `[Area]`) | Custom attributes used on ViewModels (validation/metadata) and on services/controllers (DI/routing behavior) instead of ad-hoc conventions. |
| Mapping | Mapster (`TypeAdapterConfig`) scanned across assemblies and registered as a singleton: `src/Core/Common/Startup/Startup{TUser,TRole}.cs:468-470` (`config.Scan([.. assemblies]); services.AddSingleton(config);`). In practice most entity→DTO→ViewModel mapping in services/controllers is still done by hand (see `docs/architecture/overview.md` request-flow example), not via Mapster `Adapt<T>()`. |
| Paging / filtering | `FilterListAsync<TSource>(this IQueryable<TSource>, PagingDto?)` in `src/Core/Common/Core/Extensions/Linq/QueryableExtensions.cs:64` | Applies `PagingDto.SearchFilter` (dynamic filter expressions), `SortFilter` (defaults to `Id desc`), and `PageFilter` (skip/take + optional total count) to any `IQueryable<T>` in one call. |
| Specification base classes | See §1 above | `ISpecification<T>`, `SpecificationBase<T>`, `And/Or/Not` composition. |
| Unit of work / repositories | `src/Core/Common/DataAccess/UnitOfWork/`, `src/Core/Common/DataAccess/Repositories/IRepository.cs` | Generic `IRepository<TEntity,TKey>` over `IEntityContext`; `IUnitOfWorkProvider.CreateUnitOfWork()` returns an `IUnitOfWork` wrapping the scoped `DbContext` — **all `CreateUnitOfWork()` calls within one HTTP request share the same underlying scoped `DbContext` instance** (`src/Core/Common/DataAccess/UnitOfWork/UnitOfWorkProvider.cs:31-41`); passing `trackChanges: false` mutates `ChangeTracker.QueryTrackingBehavior` for the whole request, not just that call. Do not `using`-dispose an `IUnitOfWork` — it disposes the shared scoped context. |
| Audit | `src/Core/Common/DataAccess/Audit/AuditService.cs` | Entity change auditing. |
| Data protection / opaque tokens | `src/Core/Common/Identity/ApiDataProtectorTokenProvider{TUser}.cs`, `TokenAuthenticationHandler.cs` | See `docs/architecture/cross-cutting-concerns.md`. |

Treat changes to `Core/Common` as high-leverage/high-risk: a large fraction of runtime behavior (DI wiring,
auth, model binding, paging) is defined there, and only this team maintains it (it is not a published
NuGet package with its own test suite).

## 7. Feature checklist (mirroring existing conventions)

When adding a new feature end to end:

1. **Entity** in `Domain/Entity/` + EF configuration; add an EF Core migration in
   `Infrastructure/Infrastructure/Migrations/`.
2. **Specifications** in `Domain/Specification/<Feature>/` — one class per filterable condition.
3. **DTOs** in `Core/Data/Dto/<Feature>/`.
4. **Service contract** in `Application/Interface/I<Feature>Service.cs` marked `[Injectable]` (via the
   project's assembly attribute); **implementation** in `Application/Service/<Feature>Service.cs` extending
   `LocalizableServiceBase<T>` (if it needs localized error strings) or `ServiceBase<T>`; all constructor
   dependencies as `Lazy<T>`; every public method returns `ResultData<T>`; data access via
   `UnitOfWorkProvider.Value.CreateUnitOfWork()` → `uow.GetRepository<TEntity>()` → specification-filtered,
   projected LINQ.
5. **ViewModels** in `Presentation/ViewModel/<Feature>/` using `GamaEdtech.Common.DataAnnotation` attributes
   (e.g. `[Display]`).
6. **Controller** in `Presentation/Api/Controllers/` (public) or `Presentation/Api/Areas/Admin/Controllers/`
   (admin, `[Permission(Roles = [nameof(Role.Admin)])]`) or `Areas/Finance/Controllers/`; route
   `api/v{version:apiVersion}/[controller]` (or `.../[area]/[controller]`); extend
   `ApiControllerBase<TController>`; wrap responses in `ApiResponse<T>` via `Ok<T>`/`OkWithFilter<T>`.
7. **Localization strings** in `Core/Resource/Application/<Feature>Service.resx` — referenced via
   `Localizer.Value["Key"]`.
8. **Background work**, if any → Hangfire recurring job registered in
   `src/Presentation/Api/Startup.cs` `ConfigureCore` (see the list in
   `docs/architecture/cross-cutting-concerns.md`).
9. **External integrations** → provider interface in `Infrastructure/Interface`, implementation(s) in
   `Infrastructure/Infrastructure/Provider/<Kind>/`, resolved through `IGenericFactory<TProvider, TEnum>`
   keyed by a smart enum, configured under a section in `appsettings.json`.
