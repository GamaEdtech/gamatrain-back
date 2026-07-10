# Cross-Cutting Concerns

## Authentication

Three authentication schemes are registered; there is **no JWT** anywhere in the codebase (verified: no
`Jwt`/`JsonWebToken` usage in `src/Presentation/Api` or `src/Core/Common/Identity`, despite the README's
claims to the contrary — see `ANALYZE.md` §2).

| Scheme | Registration | Handler | How the client authenticates |
|---|---|---|---|
| ASP.NET Core Identity cookie | `services.AddIdentity<TUser, TRole>(...)` in `src/Core/Common/Startup/Startup{TUser,TRole}.cs:455-462` (Identity's default cookie scheme, `IdentityConstants.ApplicationScheme`); cookie options in `src/Presentation/Api/Startup.cs:150-182` | ASP.NET Core Identity middleware | Browser session cookie (`HttpOnly`, `SameSite=None`, `Secure=Always`) |
| Custom opaque token | `services.AddAuthentication().AddScheme<TokenAuthenticationSchemeOptions, TokenAuthenticationHandler>(PermissionConstants.TokenAuthenticationScheme, ...)` — `src/Core/Common/Startup/Startup{TUser,TRole}.cs:346-348` | `TokenAuthenticationHandler` — `src/Core/Common/Identity/TokenAuthenticationHandler.cs:17-62` | `Authorization: Bearer {userId}:{dataProtectorToken}` header. The handler splits the header value on `:` into `userId` + opaque token (line 38), then calls `ITokenService.VerifyTokenAsync` with `TokenProvider = ApiDataProtectorTokenProvider`. |
| API key | `.AddScheme<ApiKeyAuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(PermissionConstants.ApiKeyAuthenticationScheme, ...)` — same line as above | `ApiKeyAuthenticationHandler` — `src/Core/Common/Identity/ApiKey/ApiKeyAuthenticationHandler.cs:26-44` | `Authorization: ApiKey {key}` header, compared against the root `ApiKey` config value (line 34); on match, issues a claim `PermissionConstants.ApiKeyPolicy = key`. |

### Opaque token mechanics
- Token issuance: `IdentityService.GenerateUserTokenAsync` (`src/Application/Service/IdentityService.cs:549-582`)
  calls `UserManager.GenerateUserTokenAsync` + `SetAuthenticationTokenAsync` (ASP.NET Core Identity's
  per-user authentication-token store) and returns `Token = $"{userId}:{token}"` (line 571) with an
  expiration computed from `ApiDataProtectorTokenProviderOptions.GetTokenLifespan(configuration)` (line 572).
- The token provider itself is `ApiDataProtectorTokenProvider<TUser>`
  (`src/Core/Common/Identity/ApiDataProtectorTokenProvider{TUser}.cs:8`), a thin subclass of ASP.NET Core
  Identity's built-in `DataProtectorTokenProvider<TUser>` — i.e. it's `IDataProtector`-encrypted opaque
  data, not a signed/self-describing JWT. It's registered as the provider for the custom purpose
  `ApiDataProtectorTokenProviderAccessToken` (`src/Core/Common/Startup/Startup{TUser,TRole}.cs:350`).
- Because `SetAuthenticationTokenAsync` is called with the same `(provider, purpose)` pair every time
  (`src/Application/Service/IdentityService.cs:563`), Identity's authentication-token store keeps **one**
  token per user for that purpose — logging in again (e.g. from a second device) overwrites the previous
  token's backing store entry. Whether that invalidates the prior token depends on how verification reads
  the store; confirm intent before relying on multi-device sessions.

### Authorization
- `PermissionAttribute` (`src/Core/Common/Identity/PermissionAttribute.cs:7-16`) extends `AuthorizeAttribute`,
  forces `AuthenticationSchemes = "{IdentityConstants.ApplicationScheme},{TokenAuthenticationScheme}"` (i.e.
  either the cookie or the opaque-token scheme can satisfy it — the API-key scheme is authorized under a
  separate policy), and adds a `Roles` property.
- The `Permission` policy (`src/Core/Common/Startup/Startup{TUser,TRole}.cs:301-327`) is a
  `RequireAssertion` that succeeds if either (a) the current user is in one of `PermissionAttribute.Roles`,
  or (b) the user has a claim of type `PermissionPolicy` whose value matches the endpoint's `DisplayName` —
  i.e. a fine-grained per-endpoint permission claim, independent of role membership.
- A second policy, `ApiKeyPolicy` (`src/Core/Common/Startup/Startup{TUser,TRole}.cs:329-344`), succeeds only
  if the endpoint carries an `ApiKeyAttribute` and the user has the `ApiKeyPolicy` claim from
  `ApiKeyAuthenticationHandler`.
- Convention: admin controllers live in `Presentation/Api/Areas/Admin/Controllers` and carry
  `[Permission(Roles = [nameof(Role.Admin)])]` at the class level, e.g.
  `src/Presentation/Api/Areas/Admin/Controllers/SchoolsController.cs:36-37`. Public controllers carry
  `[Permission(policy: null)]` at the class level (auth scheme still required, but no policy check) and
  `[AllowAnonymous]` per action that should skip auth entirely, e.g.
  `src/Presentation/Api/Controllers/SchoolsController.cs:34` and `:40`. A third area, `Areas/Finance`, holds
  finance-scoped controllers (`src/Presentation/Api/Areas/Finance/Controllers/PaymentsController.cs`).
- Cookie 401/403 responses manually set CORS headers from the request `Origin`
  (`src/Presentation/Api/Startup.cs:157-176`) instead of letting ASP.NET Core's CORS middleware handle it —
  this bypasses the configured origin allowlist (`CorsUrls` config, line 142-148) for those two response
  paths.
- `SecurityStampValidator` runs `IIdentityService.ValidatePrincipalAsync` on every request
  (`OnValidatePrincipal`, `src/Presentation/Api/Startup.cs:177-181`), i.e. a DB check per authenticated
  request for immediate revocation — a deliberate but non-trivial per-request cost.

## Background jobs (Hangfire)

- Storage: SQL Server, same connection string as the app DB —
  `services.AddHangfire(t => t.UseSqlServerStorage(Configuration.GetValue<string>("Connection:ConnectionString")))`,
  `src/Presentation/Api/Startup.cs:52-57`.
- Dashboard: `app.UseHangfireDashboard()` (`src/Presentation/Api/Startup.cs:215`) with the **default**
  `LocalRequestsOnlyAuthorizationFilter` rather than an explicit Admin-role authorization filter.
  Verify this restriction actually holds in each deployment's specific reverse-proxy/networking
  setup — an explicit Admin-role filter is the more robust option and is flagged as a hardening
  item.
- Recurring jobs registered in `ConfigureCore` (`src/Presentation/Api/Startup.cs:226-234`):

  | Job ID | Service method | Schedule |
  |---|---|---|
  | `UpdateSchoolScore` | `ISchoolService.UpdateSchoolScoreAsync()` | Weekly, Sunday 02:00 |
  | `UpdateSchoolCommentReactions` | `ISchoolService.UpdateSchoolCommentReactionsAsync(null)` | Daily 00:05 |
  | `UpdatePostReactions` | `IBlogService.UpdatePostReactionsAsync(null)` | Daily 00:10 |
  | `RemoveOldRejectedSchoolImages` | `ISchoolService.RemoveOldRejectedSchoolImagesAsync()` | Daily 00:15 |
  | `SyncCoreBoards` | `IBoardService.SyncCoreBoardsAsync()` | Daily 00:20 (a prior job `FetchCoreBoards` is explicitly removed at line 230 before this one is added) |
  | `UpdateOrphanUsers` | `IIdentityService.UpdateOrphanUsersAsync()` | Daily 00:25 |
  | `GenerateSiteMap` | `IGlobalService.GenerateSiteMapAsync()` | Daily 00:30 |
  | `UpdatePostCommentReactions` | `IBlogService.UpdatePostCommentReactionsAsync(null)` | Daily 00:35 |

  A ninth job (`IIdentityService.ConvertAvatarsAsync()`, one-off `BackgroundJob.Schedule`) is present but
  commented out (`src/Presentation/Api/Startup.cs:236`).
- Health check: `AddHangfire(t => t.MaximumJobsFailed = 5)` (`src/Presentation/Api/Startup.cs:187`).

## Caching

- **Redis**: registered via `services.AddStackExchangeRedisCache(...)` (`src/Presentation/Api/Startup.cs:59-63`),
  configured from `Cache:InstanceName` / `Cache:Configuration`. This registers `IDistributedCache` in DI;
  no direct `IDistributedCache` consumption was found in `Application/Service` or
  `Infrastructure/Infrastructure` — its concrete use today is the Hangfire/Redis health check
  (`AddRedis(...)`, `src/Presentation/Api/Startup.cs:188`).
- **ASP.NET Core output caching**: `services.AddOutputCache()` + `app.UseOutputCache()`
  (`src/Presentation/Api/Startup.cs:64,211`) is wired into the pipeline, but no controller/action currently
  carries an `[OutputCache]` attribute (verified by search) — the middleware is present but not yet applied
  to any endpoint, and there is no Redis-backed `IOutputCacheStore` registration, so if/when it is applied
  it will cache in-process only.

## Logging

- Serilog is configured directly against `IConfiguration`, not via `UseSerilog()` in `ConfigureWebHostDefaults`:
  `src/Core/Common/Hosting/Host.cs:46-51`
  ```csharp
  Log.Logger = new LoggerConfiguration()
      .ReadFrom.Configuration(config)
      .Destructure.UsingAttributes()
      .Enrich.FromLogContext()
      .Enrich.WithCorrelationId()
      .CreateLogger();
  ```
  and `ConfigureLogging(logging => { logging.ClearProviders(); logging.AddFilelog(); })` (lines 54-58) hooks
  it into the generic host's logging pipeline.
- Config (`src/Presentation/Api/appsettings.json:57-104`): async file sink (`Serilog.Sinks.Async` wrapping
  `Serilog.Sinks.File`, path `logs/log_.log`, daily rolling, size-limited roll), minimum level `Warning`
  (with per-namespace overrides, e.g. `Hangfire: Information`), and enrichers `WithMachineName`,
  `WithExceptionDetails`, `WithCorrelationId`, `WithCorrelationIdHeader`, `WithClientAgent`, `WithClientIp`
  (reading `X-Forwarded-For`).
- A last-resort `IExceptionHandler` is registered — `services.AddExceptionHandler<GlobalExceptionHandler>()`
  (`src/Core/Common/Startup/Startup{TUser,TRole}.cs:213`) — implemented at
  `src/Core/Common/Logging/GlobalExceptionHandler.cs:15-29`: it logs via `logger.LogException(exception)`,
  then still returns HTTP 200 with `{ Errors: [{ Message: exception.Message }] }`. In practice this handler
  is rarely reached because nearly every controller action and service method already wraps its body in its
  own `try/catch` and returns a `ResultData<T>`/`ApiResponse<T>` with `exc.Message` — see
  `docs/architecture/design-patterns.md` §2. `DetailedErrorsEnabled: true` is also set in the tracked
  `appsettings.json` (`src/Presentation/Api/appsettings.json:6`).

## API versioning

- `Asp.Versioning` with **URL-segment** versioning:
  `config.ApiVersionReader = new UrlSegmentApiVersionReader()` (`src/Presentation/Api/Startup.cs:71`),
  `DefaultApiVersion = 1.0`, `AssumeDefaultVersionWhenUnspecified = true`, `ReportApiVersions = true`
  (lines 68-70).
- Route templates: `api/v{version:apiVersion}/[controller]` (public,
  `src/Presentation/Api/Controllers/SchoolsController.cs:32`) and
  `api/v{version:apiVersion}/[area]/[controller]` (admin,
  `src/Presentation/Api/Areas/Admin/Controllers/SchoolsController.cs:34`); each controller also carries an
  explicit `[ApiVersion("1.0")]`.
- Swagger groups are generated per API version (`setup.GroupNameFormat = "'v'VVV"`,
  `src/Presentation/Api/Startup.cs:74`) and one `SwaggerDoc` is registered per
  `IApiVersionDescriptionProvider.ApiVersionDescriptions` entry (lines 124-139), with deprecated versions
  flagged in the description text (lines 133-136).

## Health checks

Registered in `ConfigureServicesCore` (`src/Presentation/Api/Startup.cs:184-190`):
`AddSqlServer(...)`, `AddPrivateMemoryHealthCheck(long.MaxValue)`, `AddHangfire(t => t.MaximumJobsFailed = 5)`,
`AddRedis(...)`, plus `AddHealthChecksUI(...).AddInMemoryStorage()`.

Endpoints (`ConfigureCore`, `src/Presentation/Api/Startup.cs:212-224`):

| Path | Purpose |
|---|---|
| `/health` | Plain health check endpoint (`UseHealthChecks("/health")`). |
| `/healthz` | Health check with the HealthChecks-UI JSON response writer (`UIResponseWriter.WriteHealthCheckUIResponse`), feeding... |
| `/health/details` | ...the HealthChecks UI dashboard (`MapHealthChecksUI`, `UIPath = "/health/details"`), which `.RequireAuthorization()`. |

## Localization / content localization

- General localization: `Startup<TUser,TRole>` conditionally adds `IStringLocalizerFactory` +
  `services.AddLocalization()` when `StartupOption.Localization = true` (set for this app,
  `src/Presentation/Api/Startup.cs:28`); services resolve `Lazy<IStringLocalizer<TService>>` (e.g.
  `LocalizableServiceBase<T>.Localizer`, `src/Core/Common/Service/LocalizableServiceBase.cs:15`) and read
  keys via `Localizer.Value["Key"]`, backed by `.resx` files under `Core/Resource/Application/<Service>.resx`.
- Identity errors are localized through a custom `IdentityErrorDescriber`:
  `.AddErrorDescriber<LocalizedIdentityErrorDescriber>()`
  (`src/Core/Common/Startup/Startup{TUser,TRole}.cs:462`).
- **Content localization** (translating user-facing *data*, not UI strings) is a separate feature/service:
  `IContentLocalizationService` (`src/Application/Interface/IContentLocalizationService.cs:12-19`) backed by
  the `ContentLocalization` entity (`src/Domain/Entity/ContentLocalization.cs`), with CRUD exposed through
  `Areas/Admin/Controllers/ContentLocalizationsController.cs` and a `GetLocalizedValuesAsync` lookup used to
  resolve localized field values for arbitrary content (e.g. school names) at read time.

## Configuration & secrets note

`src/Presentation/Api/appsettings.json` is tracked in git and contains what look like live credentials
(Resend API token, GamaTrain payment-gateway API key, root `ApiKey`, connection string with
`Trusted_Connection=True`). See `ANALYZE.md` §5.1 for the full list and remediation guidance — this is a
security issue, not an architecture pattern, but any new contributor reading config should know the tracked
file is not a safe source of real secrets and rotation is a P0 action item.
