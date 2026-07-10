# API Overview

Reference for the GamaEdtech backend HTTP API (ASP.NET Core, .NET 10 / C# 14, `net10.0`,
`src/Directory.Build.props:12-13`). This document covers base URL/versioning, the response
envelope, error representation, Swagger, output/response caching, and known limitations.
See `authentication.md` for auth schemes and `endpoints.md` for the full controller/action
catalog.

## Base URL and routing

Locally the app listens on `https://localhost:7001` and `http://localhost:7000`
(`src/Presentation/Api/Properties/launchSettings.json`).

Every API controller is routed as:

```
api/v{version:apiVersion}/[controller]
```

(literal attribute on each controller, e.g. `src/Presentation/Api/Controllers/SchoolsController.cs:31`).
Admin and Finance area controllers add an `[area]` segment:

```
api/v{version:apiVersion}/[area]/[controller]
```

(`src/Presentation/Api/Areas/Admin/Controllers/SchoolsController.cs:32-33`,
`src/Presentation/Api/Areas/Finance/Controllers/PaymentsController.cs:18-19`), which resolves to
`api/v1/admin/schools`, `api/v1/finance/payments`, etc. Controller/action segments are lowercased
and slugified by a custom `SlugifyParameterTransformer` registered as a route convention
(`src/Core/Common/Startup/Startup{TUser,TRole}.cs:223-227,399`), and URLs/query strings are forced
lowercase (`options.LowercaseUrls = true` / `LowercaseQueryStrings = true`, same file).

Also note: the base class `ApiControllerBase<TClass>` itself carries a generic MVC fallback route
`api/{area:slugify:exists}/{controller:slugify=Home}/{action:slugify=Index}/{id?}`
(`src/Core/Common/Core/ApiControllerBase.cs:12`), but concrete controllers override it with the
versioned `[Route]` above — the fallback only matters for the classic `HomeController`.

### API versioning

Configured with `Asp.Versioning` in `src/Presentation/Api/Startup.cs:66-76`:

- `DefaultApiVersion = 1.0`, `AssumeDefaultVersionWhenUnspecified = true`.
- Version is read from the URL segment (`UrlSegmentApiVersionReader`) — i.e. the `v{version}` in
  the route template, not a header or query string.
- `ReportApiVersions = true` (adds `api-supported-versions`/`api-deprecated-versions` response
  headers).
- Every controller currently declares `[ApiVersion("1.0")]` only — there is no `2.0` yet.

## The `ApiResponse<T>` envelope

Every action returns `Task<IActionResult<T>>`; the actual JSON body is an `ApiResponse<T>`
(`src/Core/Common/Data/ApiResponse.cs`), a `struct`:

```csharp
public struct ApiResponse<T>
{
    public T? Data { get; set; }
    public readonly bool Succeeded => Errors is null || !Errors.Any();
    public IEnumerable<Error>? Errors { get; set; }
}
```

So the wire shape is:

```json
{
  "data": { /* T, or null */ },
  "succeeded": true,
  "errors": null
}
```

`Succeeded` is a **computed** read-only property (`Errors is null || !Errors.Any()`) — there is no
independent "success" flag set by the server; a response is successful purely because `Errors` is
empty/null. `Data` can legitimately be `null` even on success (e.g. `Void` actions, or a `T` that's
naturally nullable).

List endpoints that also need paging/filter echo use a second wrapper,
`ApiResponseWithFilter<T>` (`src/Core/Common/Data/ApiResponseWithFilter.cs`), which adds:

```csharp
public IEnumerable<KeyValuePair<string, object?>>? Filters { get; set; }
```

returned via the controller's `OkWithFilter<T>(...)` helper (see e.g.
`SchoolsController.GetSchools`, `src/Presentation/Api/Controllers/SchoolsController.cs:107`).

List-shaped `T` values are usually `ListDataSource<T>` (`src/Core/Common/Data/ListDataSource{T}.cs`):

```csharp
public struct ListDataSource<T>
{
    public IEnumerable<T>? List { get; set; }
    public int? TotalRecordsCount { get; set; }
}
```

Actions with no meaningful payload return `ApiResponse<Void>`, where `Void` is a zero-size marker
struct (`src/Core/Common/Data/Void.cs`) — `data` will serialize as `{}`.

### `Error` shape

`src/Core/Common/Data/Error.cs`:

```csharp
public partial struct Error
{
    public string? Message { get; set; }
    public string? Code { get; set; }
    public string? Reference { get; set; }
    public string? Info { get; set; }
    public object? Value { get; set; }
}
```

- `Message` setter strips an embedded `**NNN**` pattern (regex `\*\*\d{3}\*\*`) out of the message
  text and, if found, promotes it to `Code` as `{ErrorCodePrefix}NNN` (`ErrorCodePrefix` = `"GAMA"`
  for this app, set in `src/Presentation/Api/Startup.cs:31`). This is how localized service error
  messages carry a machine-readable code alongside human text.
- `Reference` is populated from the model-state key when `ApiResponse<T>` is constructed directly
  from `ModelStateDictionary` (invalid model binding — see below), so on validation errors
  `Reference` names the offending field and `Value` carries the raw submitted value.

### Model validation errors

Invalid `[ApiController]` model binding (DataAnnotations failures) is intercepted globally:

```csharp
options.InvalidModelStateResponseFactory = actionContext =>
    new OkObjectResult(new ApiResponse<object>(actionContext.ModelState));
```

(`src/Core/Common/Startup/Startup{TUser,TRole}.cs:255`). Note this returns **HTTP 200**, not 400 —
consistent with the rest of the API (see "Known limitations" below). The `ApiResponse<T>(ModelStateDictionary)`
constructor (`src/Core/Common/Data/ApiResponse.cs:15-43`) flattens every model-state error into the
`Errors` list with `Reference` = the field key and `Value` = the raw submitted value.

## Swagger / OpenAPI

- Swashbuckle is wired in `src/Presentation/Api/Startup.cs:78-140` (`ConfigureSwagger`) and served
  via `app.UseSwagger()` / `app.UseSwaggerUI()` in `ConfigureCore` (`Startup.cs:195-207`).
- One Swagger doc per API version (`SwaggerDoc(description.GroupName, info)`, `Startup.cs:138`) —
  currently just `v1`.
- **Swagger UI**: default Swashbuckle route prefix (`swagger`) is not overridden anywhere, so the
  UI is reachable at **`/swagger`** (redirects to `/swagger/index.html`); the raw OpenAPI document
  is at `/swagger/v1/swagger.json` (`Startup.cs:204`).
- Two security definitions are declared for "Authorize" in the UI: `Bearer` (the custom opaque
  token — enter `Bearer <token>`) and `ApiKey` (enter `ApiKey <key>`) — both `SecuritySchemeType.Http`/`ApiKey`
  under the `Authorization` header (`Startup.cs:80-96`). The Identity cookie scheme has no Swagger
  security definition (cookies aren't something you type into Swagger's Authorize dialog).

## Output caching / response caching

- ASP.NET Core **output caching** middleware is registered and enabled globally
  (`services.AddOutputCache()` / `app.UseOutputCache()`, `Startup.cs:64,211`), but **no controller
  action currently opts in** via `[OutputCache]` — a repo-wide search under
  `src/Presentation/Api/**/*.cs` finds zero usages. The middleware is present but effectively inert
  today.
- What *is* used is the older **`[ResponseCache]`** attribute (client/shared HTTP caching via the
  `Cache-Control` header, not server-side output caching) on a handful of public, anonymous,
  read-only GET actions:
  - `BoardsController.GetBoards` — `Duration = 300` (`src/Presentation/Api/Controllers/BoardsController.cs:22`)
  - `BlogsController.GetPosts` — `Duration = 120` (`src/Presentation/Api/Controllers/BlogsController.cs:36`)
  - two more `BlogsController` actions — `Duration = 60` and `Duration = 300`
    (`src/Presentation/Api/Controllers/BlogsController.cs:98,110`)

  All use `Location = ResponseCacheLocation.Any`.

## Known limitations

- **HTTP 200 on business-logic failure.** `ApiControllerBase<T>.Ok<T>(ApiResponse<T> response)`
  (`src/Core/Common/Core/ApiControllerBase.cs:36`) always returns a `200 OkObjectResult`, whatever
  `response.Succeeded` is. Controllers almost always call `Ok(...)` even when the underlying service
  returned `OperationResult.NotFound` / `Failed` / `NotValid` / `Duplicate` — there is no
  systematic mapping from `OperationResult` to an HTTP status. **Clients must inspect the JSON body
  (`succeeded` / `errors`), never the HTTP status code, to detect failure.** `BadRequest<T>` and
  `InternalServerError<T>` helpers exist on the base class (`ApiControllerBase.cs:34,44`) but are
  rarely used by controllers in practice. Unhandled exceptions that escape the per-action
  `try/catch` are caught by `GlobalExceptionHandler`
  (`src/Core/Common/Logging/GlobalExceptionHandler.cs:17-29`), which **also forces `StatusCode =
  200`** and writes `exception.Message` straight into `Errors` — so even a genuine 500-class fault
  looks like an HTTP 200 with an `errors` array to callers.
- **`result.Data` dereferenced without checking `result.OperationResult`/`Errors` first.** A
  recurring pattern (e.g. `SchoolsController.GetSchools`,
  `src/Presentation/Api/Controllers/SchoolsController.cs:109`, and ~20 similar spots across other
  controllers) accesses `result.Data.List`/`result.Data.X` directly; if the service returned a
  failure `OperationResult`, `Data` is typically `null` and this throws a `NullReferenceException`
  — which is then swallowed by the action's own `catch (Exception)` block and surfaced to the
  client as an opaque `"Object reference not set to an instance of an object."` error message
  instead of the real failure reason. Treat any such message as this bug, not a genuine null-ref
  in your own request.
- **Exception messages leak to clients.** Both the per-action `catch (Exception exc) { ... Message
  = exc.Message }` blocks (used throughout every controller) and `GlobalExceptionHandler` return
  the raw `.Message` of server-side exceptions (potentially including SQL/provider error text) in
  the response body. Do not rely on error message *content* being safe to display verbatim to end
  users.
- No endpoint-level rate limiting is configured (no `AddRateLimiter`); Identity's built-in account
  lockout is the only brute-force mitigation on login/token endpoints. This is flagged as a
  hardening backlog item.
