# Authentication & Authorization

The API registers **three** authentication schemes side by side and a small role/claim
authorization layer on top. There is **no JWT** anywhere in the codebase, despite what the
(outdated) root README claims — verified by grepping the solution for JWT packages/usages.

| Scheme | Where configured | Used by | Typical caller |
|---|---|---|---|
| ASP.NET Core Identity **cookie** (`IdentityConstants.ApplicationScheme`) | `services.AddIdentity<TUser,TRole>()` (`src/Core/Common/Startup/Startup{TUser,TRole}.cs:455-465`) + cookie options in `src/Presentation/Api/Startup.cs:150-182` | Browser/web-app clients that call `login` | First-party web frontend |
| Custom opaque **bearer token** (`TokenAuthenticationScheme`) | `TokenAuthenticationHandler` (`src/Core/Common/Identity/TokenAuthenticationHandler.cs`), registered `src/Core/Common/Startup/Startup{TUser,TRole}.cs:346-350` | Mobile/SPA/API clients that call `tokens` | Non-browser API clients |
| **ApiKey** scheme (`ApiKeyAuthenticationScheme`) | `ApiKeyAuthenticationHandler` (`src/Core/Common/Identity/ApiKey/ApiKeyAuthenticationHandler.cs`), registered same block as above | A handful of endpoints tagged `[ApiKey]` | Trusted server-to-server callers holding the shared key |

## 1. Identity cookie scheme

- Standard `Microsoft.AspNetCore.Identity` cookie auth (`IdentityConstants.ApplicationScheme`).
  `POST /api/v1/identities/login` (`src/Presentation/Api/Controllers/IdentitiesController.cs:38-81`)
  validates username/password via `IIdentityService.AuthenticateAsync`, then calls
  `SignInAsync`, which issues the Identity cookie. `GET /api/v1/identities/logout`
  (`IdentitiesController.cs:119-138`) signs the user out.
- Cookie hardening (`src/Presentation/Api/Startup.cs:150-182`): `HttpOnly`, `SameSite=None`,
  `Secure=Always`, `ExpireTimeSpan` bound to the same `IdentityOptions:Tokens:ApiDataProtectorTokenProviderOptions:TokenLifespan`
  config value the opaque token uses (10 days, `appsettings.json:130`).
- `OnRedirectToLogin` / `OnRedirectToAccessDenied` events are overridden to return **401**/**403**
  JSON-friendly responses (instead of the default redirect-to-login-page behavior) and set CORS
  headers manually for those two responses (`Startup.cs:157-176`) rather than letting the CORS
  middleware handle them — flagged internally as fragile and worth revisiting, not detailed
  further here.
- **Per-request re-validation.** `OnValidatePrincipal` calls
  `IIdentityService.ValidatePrincipalAsync` (`Startup.cs:177-181`) on every single request carrying
  the cookie. This is driven by `SecurityStampValidatorOptions.ValidationInterval = 00:00:00`
  (`appsettings.json:133-135`, bound at `Startup{TUser,TRole}.cs:464`) — i.e. the interval is
  effectively zero, so the security stamp is checked against the DB on every request rather than
  cached for a window. Effect: revoking a user (password change, lockout, role change) takes effect
  immediately, at the cost of a DB round-trip per authenticated request.
- Password/account/lockout policy is configured under `IdentityOptions` in `appsettings.json` —
  see [`docs/business/identity-and-access.md`](../business/identity-and-access.md) for the
  business-level read (currently more permissive than recommended; exact values intentionally not
  enumerated here).

## 2. Custom opaque bearer token scheme

This is the scheme most non-browser API clients use.

**Obtaining a token** — `POST /api/v1/identities/tokens`
(`src/Presentation/Api/Controllers/IdentitiesController.cs:161-207`, `[AllowAnonymous]`):
1. Body: `GenerateTokenRequestViewModel` (`Username`, `Password`).
2. Controller calls `IIdentityService.AuthenticateAsync` (local username/password check), then
   `IIdentityService.GenerateUserTokenAsync` (`src/Application/Service/IdentityService.cs:549-582`).
3. `GenerateUserTokenAsync` calls ASP.NET Identity's `UserManager.GenerateUserTokenAsync` with
   `TokenProvider = "ApiDataProtectorTokenProvider"` and
   `Purpose = "ApiDataProtectorTokenProviderAccessToken"` (constants in
   `src/Core/Common/Identity/PermissionConstants.cs:9-10`), backed by a custom
   `ApiDataProtectorTokenProvider<TUser>` (`src/Core/Common/Identity/ApiDataProtectorTokenProvider{TUser}.cs`)
   registered against `IdentityOptions.Tokens.ProviderMap` (`Startup{TUser,TRole}.cs:350`).
4. The raw provider token is then persisted via `UserManager.SetAuthenticationTokenAsync` under the
   same provider/purpose, and the response token returned to the client is:
   ```
   {userId}|{providerToken}
   ```
   — the delimiter is `Constants.DelimiterAlternate = "|"` (`src/Core/Common/Core/Constants.cs:17`),
   concatenated at `IdentityService.cs:568` (ANALYZE.md's writeup describes this as `{userId}:{token}`;
   the literal separator in code is `|`, not `:`).
5. Response `GenerateTokenResponseViewModel` carries `Token` and `ExpirationTime` (now +
   `IdentityOptions:Tokens:ApiDataProtectorTokenProviderOptions:TokenLifespan`, 10 days by default,
   `appsettings.json:128-131`).

Two alternate token-issuing endpoints exist, both `[AllowAnonymous]`:
- `POST /api/v1/identities/tokens/old` — exchanges a legacy "core" token for a new one via
  `GenerateTokenByCoreTokenAsync` (explicitly commented `// this is temporary, must delete`,
  `IdentitiesController.cs:209-251`). Requires the caller to already hold a legacy token; does
  **not** create a local user if none is found by email — see the legacy-auth-bridge below for the
  endpoint that replaces this one. Validates the incoming legacy JWT's signature via the same
  `Core:JwtSigningSecret`-backed check the bridge uses (`ValidateLegacyJwtAsync`) — this used to skip
  signature validation entirely (a real forgeable-token gap, closed alongside the bridge work below).
- `POST /api/v1/identities/tokens/google` — exchanges a Google OAuth code/id-token for a token via
  the same `AuthenticateAsync` + `GenerateUserTokenAsync` pipeline, with
  `AuthenticationProvider.Google`.

**Presenting a token** — send `Authorization: Bearer {userId}|{providerToken}` (or a gama-api JWT,
see below) on any request. `TokenAuthenticationHandler.HandleAuthenticateAsync`
(`src/Core/Common/Identity/TokenAuthenticationHandler.cs:30-61`):
1. Strips the `Bearer ` prefix.
2. Splits on `|` into exactly 2 parts (`userId`, `token`). A gama-api JWT never contains `|` (it's
   base64url), so this reliably tells the two token shapes apart.
3. If it split into 2 parts, calls `ITokenService.VerifyTokenAsync` with the same provider/purpose
   constants used to mint it (the normal path). Otherwise calls
   `ITokenService.VerifyLegacyTokenAsync` — see the legacy-auth-bridge section below.
4. On success, builds a `ClaimsPrincipal` from the returned claims and issues an
   `AuthenticationTicket` under this scheme's name.
5. Any malformed header, unknown user, or failed verification yields `AuthenticateResult.NoResult()`
   (not `Fail`) — i.e. the request falls through as unauthenticated rather than erroring.

### Legacy-auth bridge (temporary)

`LegacyAuthBridgeController` (`src/Presentation/Api/Controllers/LegacyAuthBridgeController.cs`,
route `api/v1/legacy-auth`) proxies gama-api's (the old PHP backend)
`login`/`register`/`recovery`/`googleAuth`/`group` endpoints so the frontend can migrate off
gama-api one flow at a time, while both backends stay usable during the transition. Slated for
removal — alongside `tokens/old` above — once the frontend fully migrates. `[AllowAnonymous]` is
applied per-action rather than on the class: every action is anonymous except `group`, which needs
the caller's resolved local user and so is gated by `[Permission(policy: null)]` instead (a
class-level `[AllowAnonymous]` would otherwise unconditionally win over any per-action
`[Authorize]`-derived attribute).

- `POST login` / `POST google` proxy gama-api's `/users/login` / `/users/googleAuth`
  (`ICoreProvider.LegacyLoginAsync`/`LegacyGoogleAuthAsync`,
  `src/Infrastructure/Infrastructure/Provider/Core/CoreProvider.cs`). On success (gama-api returns
  `jwtToken` + `info`), `IdentityService.SyncLegacyAuthAsync` decodes and **cryptographically
  verifies** the legacy JWT (`ValidateLegacyJwtAsync`, shared with the two call sites below) to get
  `CoreId`/identity, finds the local `ApplicationUser` by `CoreId` → email → phone (falling back
  rather than erroring, so a pre-existing native account gets **linked**, not duplicated), creates
  one via the normal `UserManager.CreateAsync` path if none matches — and hands gama-api's
  `jwtToken` straight back to the frontend, **unchanged**. No gamatrain-back token is minted for
  this flow at all.
  - **`login` OTP step-up (undocumented in gama-api's OpenAPI spec, found by live testing).** For a
    weak/easy-to-guess password, gama-api doesn't return a token at all — it responds
    `{"status":1,"data":{"type":"loginByOTP"}}` and sends a fresh OTP to the identity, invalidating
    any previous one (every plain `login` call resends). `LegacyLoginRequestDto`/
    `LegacyLoginRequestViewModel` accept optional `Type`/`Code` fields for this: the bridge relays
    `{"type":"loginByOTP"}` back to the frontend as a **successful, actionable** response (not an
    error — `LegacyBridgeTokenResponseDto.Type` set, `Token` null), and the frontend resubmits
    `login` with `type: "confirm"` + the received `code` (`identity`/`pass` still required) to
    complete it, at which point gama-api returns the normal `jwtToken`+`info` shape and sync/return
    proceeds as usual.
- `POST register` / `POST recovery` proxy gama-api's `/users/register` / `/users/recovery`
  (`ICoreProvider.LegacyRegisterAsync`/`LegacyRecoveryAsync`) as **pure passthroughs** — no local
  user sync, no token minted. Both are multi-step OTP flows on gama-api's side
  (`type`: `request`/`resend_code`/`confirm`/final), and neither ever returns a token at any step
  (`{"status":1,"data":{"message":"done"}}` even on the final step) — the frontend calls `login`
  afterward to actually get a session, which is where sync happens.
- **`login`/`google`/`register`/`recovery` forward the caller's real IP to gama-api as
  `TRUSTED_FORWARDED_IP`.** Since this backend proxies the request, gama-api's own
  rate-limiting/fraud checks would otherwise only ever see this server's IP, never the end user's.
  `IdentityService` reads the client IP off the inbound request (`HttpContext.GetClientIpAddress()`
  — trusts an existing `X-Forwarded-For` header if present, else the raw connection IP) and sets it
  on `LegacyLoginRequestDto`/`LegacyGoogleAuthRequestDto`/`LegacyOtpFlowRequestDto` before calling
  `ICoreProvider`; `CoreProvider` adds it as a `TRUSTED_FORWARDED_IP` header on the outgoing gama-api
  call (`Constants.TrustedForwardedIp`). `logout` doesn't send it — gama-api didn't ask for it there.
- `GET logout` proxies gama-api's `GET /users/logout` (`ICoreProvider.LegacyLogoutAsync`,
  `Core:Logout` config) as a **pure passthrough** — same shape as register/recovery. The caller's
  raw legacy JWT is read straight from the incoming `Authorization` header
  (`TokenAuthenticationHandler.GetTokenFromHeader`) and relayed unchanged as gama-api's own
  `bearerAuth`; this backend never stored the token, so there's nothing local to update — gama-api
  is the one that actually invalidates the session server-side. This is the one legacy-bridge
  operation that **does** end a session early, unlike the trade-off described below for
  `tokens/revoke`.
- `POST group` (`[Permission(policy: null)]` — requires login, the only action on this controller
  that does) proxies gama-api's `POST /users/group` (`ICoreProvider.LegacyUpdateGroupAsync`,
  `Core:UpdateGroup` config) to let the caller set their own `Group` (5 = Teacher, 6 = Student — see
  `docs/business/identity-and-access.md`'s "User type" section) without waiting for their next
  legacy login. The caller's local user id comes from `User.UserId()` (populated by
  `TokenAuthenticationHandler` same as any other authenticated request); the raw token is
  separately read via `TokenAuthenticationHandler.GetTokenFromHeader` and forwarded to gama-api,
  same as `logout`. gama-api's own optional `uid` form field (targets an arbitrary user) is
  deliberately never sent — only `token`+`group` — so gama-api always infers the target from the
  token itself and this proxy can only ever act on the caller's own account. On success,
  `IdentityService.LegacyUpdateGroupAsync` updates the local `ApplicationUser.Group` and
  immediately re-runs the same `SyncRoleFromGroupAsync` role-sync legacy login triggers.

**Why no wrapping.** The natural design would be to mint a gamatrain-back token and hand back some
combination of the two. Instead, gamatrain-back adapts to gama-api's token instead of the other way
around: `ITokenService.VerifyLegacyTokenAsync` (`IdentityService.cs`) validates an incoming gama-api
JWT directly and resolves it to the local user already linked by `CoreId`. The frontend ends up
holding exactly one token, identical to what it already gets from gama-api today, usable unchanged
against **both** backends. gama-api needs zero code changes, since it never sees anything but its
own token in its own format.

**Signature verification is real, not skipped — this requires a shared secret.** All three
JWT-accepting code paths (`VerifyLegacyTokenAsync`, `SyncLegacyAuthAsync`,
`GenerateTokenByCoreTokenAsync`/`tokens/old`) go through one shared helper,
`IdentityService.ValidateLegacyJwtAsync`, which checks issuer, audience, expiry, **and** the
token's HS256 signature against `Core:JwtSigningSecret`. Without real signature verification, anyone
could hand-craft a JSON object with the right issuer/audience/expiry/`user_id` claims and a garbage
signature and it would be accepted as genuine — a full account-takeover path for any user who's ever
been linked via `CoreId`. (Earlier revisions of this bridge, and the pre-existing `tokens/old`
endpoint before this change, skipped signature validation entirely — `Core:JwtSigningSecret` must be
the real HS256 key gama-api signs with, obtained from their team out-of-band; it is **not**
populated in the tracked `appsettings.json`, empty by default per the repo's "never commit a real
secret" rule, and every legacy-JWT code path fails closed — rejects the token — until it's set.)

**Trade-offs of this approach** (accepted deliberately, worth knowing if debugging a legacy-bridge
session):
- **Session lifetime is gama-api's, not ours.** A legacy-bridge session lives until the JWT's own
  `exp` claim (~30 days per observed samples), not the configurable
  `IdentityOptions:Tokens:ApiDataProtectorTokenProviderOptions:TokenLifespan` that governs normal
  opaque-token sessions.
- **`tokens/revoke` cannot end a legacy-bridge session early.** JWTs are self-contained/stateless —
  there is no server-side store *here* to invalidate. This only affects sessions started via
  `legacy-auth/login`/`google`; native opaque-token sessions revoke exactly as before. Use
  **`GET legacy-auth/logout`** instead for a legacy-bridge session — it proxies gama-api's own
  logout, which does hold server-side state on gama-api's side even though this backend doesn't.

**Revocation** — `POST /api/v1/identities/tokens/revoke` (`[Permission(policy: null)]`, i.e.
requires being authenticated first) invalidates the current token
(`IdentitiesController.cs:300-324`).

**Caveat (see ANALYZE.md B7):** `SetAuthenticationTokenAsync` is called with the same
provider/purpose pair every time a token is generated for a user — Identity's token store keeps
one token per `(user, provider, purpose)` tuple. Generating a new token for a user (e.g. logging in
on a second device) overwrites the previous one; whether the old token is then rejected depends on
the underlying provider's validation semantics — treat "one active token per user" as the working
assumption until verified otherwise, and avoid depending on multiple concurrently valid tokens for
the same account.

Both the Identity cookie scheme and this token scheme are accepted together wherever
`[Permission(...)]` is used — `PermissionAttribute` sets
`AuthenticationSchemes = "{IdentityConstants.ApplicationScheme},{TokenAuthenticationScheme}"`
(`src/Core/Common/Identity/PermissionAttribute.cs:12`), so a request authenticates if **either**
the cookie or the bearer token validates.

## 3. ApiKey scheme

A single shared secret, used to protect a small number of trusted server-to-server or
"no user context" endpoints — distinct from per-user auth entirely.

- Handler: `ApiKeyAuthenticationHandler` (`src/Core/Common/Identity/ApiKey/ApiKeyAuthenticationHandler.cs`).
  Expects `Authorization: ApiKey {key}` and compares the literal key against the root-level
  `"ApiKey"` config value (`configuration.GetValue<string?>("ApiKey")`,
  `ApiKeyAuthenticationHandler.cs:34`). **Do not treat any value currently in a tracked
  `appsettings.json` as a real secret you can rely on being secret** — see
  [`docs/deployment/configuration.md`](../deployment/configuration.md) for the general secrets
  callout. Rotate and externalize this value before depending on the ApiKey scheme in production.
- On success it issues a `ClaimsPrincipal` with a single claim
  `(PermissionConstants.ApiKeyPolicy, key)` — no user identity, no roles.
- Applied via the `[ApiKey]` attribute (`src/Core/Common/Identity/ApiKey/ApiKeyAttribute.cs`), which
  sets `Policy = "ApiKey"` and `AuthenticationSchemes = "ApiKeyAuthenticationScheme"`.
- Current real usage: `GET /api/v1/games/easter-egg/fortune-wheel`
  (`src/Presentation/Api/Controllers/GamesController.cs:24-26`) is the only controller action in
  the whole API gated by `[ApiKey]` (verified by grep across `src/Presentation`). It is otherwise
  used conceptually to protect the GamaTrain Solana payment-gateway's transaction-details lookup
  from the *provider* side (`PaymentGateway.GamaTrain.ApiKey` in config, a *different* key from the
  root `ApiKey` — don't confuse the two; the provider-side key authenticates this backend as a
  client of the payment gateway, while the root `ApiKey` authenticates external callers *into* this
  backend).

## Authorization: the `Permission` policy and role gates

Two custom `AuthorizeAttribute` subclasses drive everything:

- **`PermissionAttribute`** (`src/Core/Common/Identity/PermissionAttribute.cs`) — default policy
  name `"Permission"`, accepts an optional `Roles` array. Used as `[Permission(policy: null)]`
  (any authenticated user, no role check) or `[Permission(Roles = [nameof(Role.Admin)])]`
  (must be in that role).
- **`ApiKeyAttribute`** — policy `"ApiKey"`, described above.

Both policies are registered with `RequireAssertion` handlers in
`src/Core/Common/Startup/Startup{TUser,TRole}.cs:301-344`:

- **`"Permission"` policy** (lines 301-327): reads the current endpoint's `PermissionAttribute`
  metadata (`LastOrDefault()` — if a controller and an action both carry one, the action's wins).
  Passes if either (a) `permission.Roles` is non-empty and the user is in any of those roles
  (`context.User.IsInRole(t)`), **or** (b) the user has a claim of type `"Permission"` whose value
  case-insensitively equals the endpoint's `DisplayName` — i.e. fine-grained, per-endpoint
  permission claims can be granted to a user independently of role membership (this is what backs
  the Admin `PUT /api/v1/admin/identities/{userId}/permissions` action for assigning individual
  endpoint permissions to non-Admin users).
- **`"ApiKey"` policy** (lines 329-344): passes if the endpoint carries an `ApiKeyAttribute` and the
  authenticated principal has an `"ApiKey"`-typed claim (which only the `ApiKeyAuthenticationHandler`
  issues).

`Role` (`src/Domain/Enumeration/Role.cs`) is a flags-style smart enum with five members:
`Admin`, `Teacher`, `Student`, `Advisor`, `Finance`.

### How endpoint groups are gated, concretely

- **Public controllers** (`src/Presentation/Api/Controllers/*`): almost all declare class-level
  `[Permission(policy: null)]` (any authenticated user by default), then use `[AllowAnonymous]` on
  individual actions to open up specific reads (e.g. `SchoolsController.GetSchools`,
  `IdentitiesController.Login`/`Register`/`GenerateToken`). Several controllers instead put
  `[AllowAnonymous]` at the **class** level (`BoardsController`, `LocationsController`,
  `SubjectsController`, `TagsController`, `TopicsController`, `GradesController`,
  `VotingPowersController`, `LanguagesController`, `FilesController`) — these are anonymous end to
  end at the HTTP-auth-attribute layer; `VotingPowersController`'s bulk-import `POST` additionally
  verifies an in-body signature as its access control instead of a standard auth attribute (see
  `endpoints.md`).
  `IdentitiesController` and `GamesController` and `HomeController` and `ExamsController` skip the
  class-level attribute entirely and annotate every action individually (see `endpoints.md` for the
  per-action breakdown); `HomeController` in particular has **no auth attribute anywhere** in the
  file and no `[Route]`/`[ApiVersion]` either — it's a thin non-API MVC controller that redirects
  `/` to `/swagger` (`src/Presentation/Api/Controllers/HomeController.cs`), not a documented API
  surface.
- **Admin controllers** (`src/Presentation/Api/Areas/Admin/Controllers/*`): every one of the 18
  files declares class-level `[Permission(Roles = [nameof(Role.Admin)])]` plus
  `[Common.DataAnnotation.Area(nameof(Admin), "Admin")]` and route
  `api/v{version:apiVersion}/[area]/[controller]` (resolving to `api/v1/admin/...`). No action in
  any Admin controller carries `[AllowAnonymous]` or a different role — the whole area is uniformly
  Admin-only.
- **Finance area** (`src/Presentation/Api/Areas/Finance/Controllers/PaymentsController.cs`): same
  route shape (`api/v1/finance/payments`), but gated by
  `[Permission(Roles = [nameof(Role.Finance)])]` — a **different** role from Admin. An Admin user
  who is not also granted the `Finance` role (or an equivalent per-endpoint permission claim) cannot
  call it.

### Practical implications for API consumers

- To call anything under `Areas/Admin`, authenticate as a user in the `Admin` role (cookie or
  bearer token — both schemes are accepted per `PermissionAttribute`'s `AuthenticationSchemes`).
- To call `Areas/Finance`, the user must be in the `Finance` role specifically (or hold a matching
  per-endpoint `Permission` claim assigned via the Admin identities endpoint).
- To call `[ApiKey]`-gated actions, send the shared key as `Authorization: ApiKey {key}` — no user
  session is involved or created.
- For everything else, check the action's own attributes in `endpoints.md` — `[AllowAnonymous]`
  always wins over a class-level `[Permission]`, but the reverse (a class-level
  `[AllowAnonymous]` with a stricter action-level attribute) is not used anywhere in this codebase.
