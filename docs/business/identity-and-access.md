# Identity & Access

Business logic: `src/Application/Service/IdentityService.cs` (~1937 lines),
contract: `src/Application/Interface/IIdentityService.cs`. Entities:
`src/Domain/Entity/Identity/` (`ApplicationUser`, `ApplicationRole`, plus
standard Identity join/claim/token tables).

## Registration & login

- **Registration** — `IdentityService.RegisterAsync`
  (`IdentityService.cs:280-308`) creates an `ApplicationUser` with
  `Enabled = true` and `ProfileVisibility.Private`, delegating password/
  username validation to ASP.NET Core Identity's `UserManager`. Duplicate
  usernames are caught via `UniqueConstraintException`
  (`IdentityService.cs:299-301`). No role is assigned at registration.
  `SendRegistrationEmailAsync` (`:310-327`) sends a templated welcome/
  confirmation email.
- **Admin-created users** — `CreateUserAsync` (`:382-422`) sets
  `EmailConfirmed`/`PhoneNumberConfirmed` to `true` directly, bypassing the
  normal confirmation flow.
- **Sign-in** — `SignInAsync` (`:329-365`) builds a claims principal
  (email, phone, a device-hash claim, a timezone claim, one role claim per
  role) and signs in via `SignInManager.SignInWithClaimsAsync` (cookie
  auth). Cookie principals are **revalidated on every request**
  (`ValidatePrincipalAsync`, driven by `SecurityStampValidator`, see below).
- **Custom opaque bearer token** — in addition to the cookie scheme, the
  API exposes a token scheme built on ASP.NET Identity's user-token
  infrastructure: `GenerateUserTokenAsync` (`:549-582`) wraps
  `userManager.GenerateUserTokenAsync` and returns `"{UserId}|{token}"`
  (delimiter `Constants.DelimiterAlternate`, `src/Core/Common/Core/Constants.cs:17`),
  read back by `TokenAuthenticationHandler.cs:38`. `VerifyTokenAsync`
  (`:584-631`) validates it. There is no JWT anywhere in the codebase
  despite what the project README claims (see `ANALYZE.md` §2).
- **Password change/reset** — `ChangePasswordAsync` (`:653-672`) requires
  the current password (self-service). `ResetPasswordAsync` (`:674-694`)
  generates and immediately consumes an Identity reset token internally in
  one call — i.e. it is an admin/trusted-flow reset, not the classic
  "email a reset link, then submit a new password" two-step flow.

## Legacy-auth bridge (temporary, migration-only)

While gama-api (the old backend) is still in use, `LegacyAuthBridgeController`
(`api/v1/legacy-auth`) proxies its `login`/`register`/`recovery`/`googleAuth`/`logout`/`group`
flows so users who only ever had an old-backend account can keep authenticating without a separate
"migrate your account" step. On a successful `login`/`google` call, `IdentityService.SyncLegacyAuthAsync`
(`IdentityService.cs`) links or creates the local `ApplicationUser`:

1. Look up by `CoreId` (the existing FK linking a local user to their old-backend id).
2. Fall back to matching by email, then by phone — this is what lets a user who already has a
   *native* gamatrain-back account (registered before or independent of this bridge) get their
   `CoreId` attached to their existing account on first legacy login, instead of ending up with two
   separate accounts for the same person.
3. Only creates a new `ApplicationUser` if none of the above match.

`register`/`recovery` never trigger this — gama-api's own OTP flows never return a token or profile
data at any step, so there is nothing to sync until the user actually logs in afterward.

On success, `login`/`google` hand gama-api's own token back to the frontend **unchanged** — no new
gamatrain-back token is minted. `ITokenService.VerifyLegacyTokenAsync` lets gamatrain-back accept
that same token directly on later requests (resolved to the local user via `CoreId`), so gama-api
never has to change anything and the frontend never has to know two backends are involved — see
[`docs/api/authentication.md`](../api/authentication.md)'s "Legacy-auth bridge" section for the
mechanism, its required `Core:JwtSigningSecret` (real signature verification, not optional — a
forged token otherwise authenticates as any linked account), and its trade-offs (notably: a
legacy-bridge session can't be revoked early via `tokens/revoke`, since it isn't backed by any
server-side token store here — `GET legacy-auth/logout` covers that case instead, by proxying
gama-api's own logout rather than relying on local state). This whole bridge —
controller, the `Legacy*` methods on `ICoreProvider`/`IIdentityService`, and
`VerifyLegacyTokenAsync` — is temporary and will be removed once the frontend fully migrates off
gama-api.

`POST legacy-auth/group` (added 2026-08-22) proxies gama-api's own `POST /users/group` to let the
caller set their own `Group` (5 = Teacher, 6 = Student — see "User type" below) without waiting for
their next legacy login. Unlike the other actions on this controller it requires a resolved local
user, so it's the one action here gated by `[Permission(policy: null)]` instead of
`[AllowAnonymous]` — `TokenAuthenticationHandler` resolves the caller's forwarded legacy JWT to the
local `ApplicationUser` the same way it does for any other authenticated endpoint (this is also why
`[AllowAnonymous]` moved from the controller class down to each of the other individual actions —
a class-level `[AllowAnonymous]` can't be overridden by a per-action `[Authorize]`-derived
attribute). gama-api's own `uid` form field (lets a caller target an arbitrary user) is
deliberately never sent — `ICoreProvider.LegacyUpdateGroupAsync` only ever forwards `token`+
`group`, so gama-api infers the target user from the token itself and this proxy can only ever act
on the caller's own account. On a successful call, `IdentityService.LegacyUpdateGroupAsync` updates
the local `ApplicationUser.Group` and immediately re-runs `SyncRoleFromGroupAsync` (see below),
instead of leaving the local copy stale until the next legacy login re-syncs it.

## User type (`ApplicationUser.Group`) — not the same concept as `Role` below

`Group` (`int?`, `ApplicationUser.cs`) is the actual signal for "is this person a teacher or a
student" — confirmed live (2026-08-22) against production data and the frontend's own source
(`Gamaedtech-frontv3/app/types/user/index.ts`): **`Group = 5` is Teacher, `Group = 6` is
Student**. `Group = 3` is reserved for a third type (redirects to `/test-maker` in the frontend's
`user_type.ts` middleware instead of the Teacher/Student onboarding page) but had zero real users
as of the same check. Everything else (`NULL`, `1`, `2`, `7` — `2` alone was ~87% of all users)
falls through to the frontend's `/user/type` onboarding page, where picking Teacher/Student is
presumably how someone ends up as `5`/`6` in the first place; this backend has no local
enum/definition of what those other values mean, since it doesn't need to interpret them, only
pass them through.

**Do not confuse this with the `Role.Teacher`/`Role.Student` values documented right below** —
same words, completely different mechanism. `Role` is this app's own RBAC/permission system
(`ApplicationUserRoles`, checked via `User.IsInRole(...)`); `Group` is opaque data mirrored from
gama-api. In practice `Role.Teacher`/`Role.Student` are essentially unassigned in real data (every
single one of the ~28,900 production users checked came back with no role at all, regardless of
their `Group` value) — `Group` is what the system actually uses to distinguish teacher/student
today, not `Role`.

`Group` is set once at first legacy-auth sync like the other profile fields (`FirstName`,
`Gender`, etc. — see "Legacy-auth bridge" above) with one exception: **it's the only field
`SyncLegacyAuthAsync` re-syncs on every single legacy login**, not just the first one
(`IdentityService.cs`, `user.Group = authData.Group;` sits outside the `!user.ProfileUpdated`
guard the other fields are behind). So unlike the rest of a synced profile, which this app owns
after the first login, gama-api can still change a user's `Group` at any time and it'll take
effect here the next time they log in through the legacy bridge.

**`Role.Teacher`/`Role.Student` are now kept in sync with `Group` automatically** (added
2026-08-22, `IdentityService.SyncRoleFromGroupAsync`, called right after `Group` is set/updated in
both branches of `SyncLegacyAuthAsync`). Deliberately additive-plus-swap, not a full role replace:
adds the matching role (`Teacher` for `Group = 5`, `Student` for `Group = 6`) if the user doesn't
already have it, and removes the *other* of Teacher/Student if present — so `Role` stays an
accurate mirror of `Group` even if it changes later — but never touches any other role
(`Admin`/`Advisor`/`Finance`); since `Role` is a flags enum, a user who's also an `Admin` keeps
that role regardless. `Group` values with no known mapping (`NULL`, `1`, `2`, `3`, `7`) leave
existing Teacher/Student role membership untouched — guessing a removal for an unrecognized value
would be worse than doing nothing. Best-effort: a failure here is logged and swallowed, never
fails the login itself. This only fires going forward, on each legacy login — it does not itself
backfill role assignment for users who predate it; see `IdentityService.
BackfillRoleAndProfileVisibilityFromGroupAsync` for the separate one-time catch-up (PR #609).

`CoreProvider.cs` reads this off gama-api's own response via `info?.Group.ValueOf<int?>()` — on
gama-api's side it's apparently a real enum/smart-enum type; this app only ever sees and stores
the flattened raw integer, never gama-api's own type definition, which is why this repo has no
local named constants for it beyond the confirmed `5`/`6`.

## Roles

`Role` (`src/Domain/Enumeration/Role.cs:11-23`) is a **flags** smart enum
(`FlagsEnumeration<Role>`), so a user can hold multiple roles as a bitmask:
`Admin=1`, `Teacher=2`, `Student=3`, `Advisor=4`, `Finance=5`. Role
assignment is not part of registration — it happens via
`UpdateUserPermissionsAsync` (`IdentityService.cs:761-882`), which diffs
requested vs current roles, calls `AddToRolesAsync`/`RemoveFromRolesAsync`,
forces a logout on any role change (`:795, 801`), and refuses to remove the
last remaining Admin (`:776-783`, error `LastAdminCantBeRemoved`). Admin vs
regular-user endpoints are gated at the controller level via
`[Permission(Roles = [nameof(Role.Admin)])]` on Admin-area controllers,
versus `[Permission(policy: null)]`/`[AllowAnonymous]` on public ones (see
`ANALYZE.md` §3).

A separate `SystemClaim` flags enum (`src/Domain/Enumeration/SystemClaim.cs:11-27`)
grants individual users **auto-approval** rights for their own
contributions — `AutoConfirmSchoolContribution`, `AutoConfirmSchoolImage`,
`AutoConfirmSchoolComment`, `AutoConfirmPost`, `AutoConfirmRemoveSchoolImage`,
`AutoConfirmPostComment` — bypassing the normal admin-review step described
in `docs/business/schools-directory.md` and `docs/business/exams-and-content.md`.

## Experience

`Experience` (`src/Domain/Entity/Experience.cs`) is **not** a generic
resume/work-history field — it specifically models a user's affiliation
with a school: `UserId`, `SchoolId` (both required), `StartDate` (required),
optional `EndDate`, and a free-text `Description`. Managed via
`ExperienceService.cs`: `GetExperiencesAsync` (list, `:27`),
`GetExperienceAsync` (single, `:51`), `ManageExperienceAsync` (upsert,
`:80`), `RemoveExperienceAsync` (`:130`).

## Account security posture

Identity's password, lockout, and account-confirmation policy is configured in
`src/Presentation/Api/appsettings.json` under `IdentityOptions` (`Password`, `Lockout`, `SignIn`,
`User`, `Tokens`, `SecurityStampValidator` sections — see
[`docs/deployment/configuration.md`](../deployment/configuration.md) for the section-name-only
config catalog). Directionally, the current policy is **more permissive than typical for a
platform that may handle minors' data** (short minimum password length with no complexity
requirement, no required email/account confirmation before use). This is flagged as a hardening
backlog item — exact configured values are intentionally not enumerated in this public document;
see the internal (untracked) technical review for specifics before treating the current policy as
a deliberate, reviewed decision.

`SecurityStampValidator.ValidationInterval` is set low enough that principal revalidation happens
on effectively every authenticated request (immediate revocation on password/role change, at a
per-request DB cost) rather than being cached for a window.

## Audit trail: LoginHistory

`LoginHistory` (`src/Domain/Entity/LoginHistory.cs`): `UserId`,
`CreationDate`, `IpAddress` (required, max 50 chars), `UserAgent` (optional,
max 500 chars). Written by `AddLoginHistoryAsync`
(`IdentityService.cs:1393-1410`), which also updates
`ApplicationUser.LastLoginDate` in the same call. No failure/success flag
exists — only successful sign-ins appear to produce a record (invoked from
`IdentitiesController` after authentication succeeds). See
`docs/business/support-and-social.md` for more on this and other audit-adjacent
data.
