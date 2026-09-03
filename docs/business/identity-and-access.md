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

## User dashboard proxy (`identities/dashboard`)

`IdentitiesController.GetDashboard` (`GET api/v1/identities/dashboard`) gives gamatrain-front's
user dashboard page (`app/pages/user/index.vue`) a single merged payload from this backend,
replacing its previous direct calls to gama-api's `GET /teachers/dashboard` / `GET
/students/dashboard`. Unlike the legacy-auth bridge above, this endpoint lives on the regular
`IdentitiesController` and is gated by the normal `[Permission(policy: null)]` — it is **not** part
of the temporary bridge and is not itself slated for removal; it reuses the bridge's
"forward the caller's raw legacy JWT to gama-api" mechanism (see
[`docs/api/authentication.md`](../api/authentication.md)'s "Legacy-auth bridge" section) purely as
a data source, the same way `GetUserInformationAsync`/`GetBoardsAsync` already do.

**Phase 0** (2026-09-01) was a field-for-field passthrough of gama-api's whole dashboard response.
**Phase 2** (same day, immediately after — no separate Phase 1 subscription-only step ended up
happening, subscription data landed directly in this rework instead) replaced that with the current
shape: `User`/`ProfileCompletion`/`UnreadMessages` are now built **entirely from this backend's own
data** — always populated, independent of gama-api entirely. Only `Stats`/`ExamSuggestions`, plus the
one remaining `User` field with no local equivalent (`ScoreCheckInfo`), still have no local domain
to source them from and stay proxied from gama-api — see
`DashboardResponseDto`'s doc comment for the authoritative field-by-field split. Concretely, per
gama-api field:

| gama-api field | Now sourced from | Note |
|---|---|---|
| `id` | `ApplicationUser.CoreId` → `coreId` | |
| `group_id` | `ApplicationUserRole` → `Role` names → `roles` | this app's real RBAC, not gama-api's opaque signal |
| `username` | `ApplicationUser.Handle` → `handle` | |
| `first_name`/`last_name`/`phone`/`avatar` | `ApplicationUser.FirstName`/`LastName`/`PhoneNumber`/`AvatarId` (→ real URL via `IFileService.GetStaticFileUrl`, same helper `profiles` GET already uses) | |
| `sex` | `ApplicationUser.Gender` | a real enum locally, not gama-api's raw `"1"`/`"2"` code |
| `active` | `ApplicationUser.Enabled` | |
| `score` | `ApplicationUser.CurrentBalance` → `points` | this backend's own points ledger, the same value `leader-board` ranks by — **not the same number as gama-api's own legacy score** |
| `state`/`city`/`school` | `CityId`/`SchoolId` + resolved `City.Title`/`School.Name` → `cityId`/`cityTitle`/`schoolId`/`schoolTitle` | |
| `credit` | `ISubscriptionQuotaService.GetCurrentSubscriptionAsync` → `subscription` (full `UserSubscriptionDto`, same shape `subscriptions/me` returns) | `credit` had no real local equivalent; subscription is what actually belongs here — `null` on the free tier, a normal state, not an error |
| `profileCompletion` | `UserRateLevel.Calculate`'s own signals (avatar/firstName/lastName/currentStatusSentence/biography/skills/experience), repackaged as `{total,num,notComplete[]}` | same shape gama-api used, same underlying "what's missing" concept, entirely local — `BuildDashboardProfileCompletionAsync` |
| `unreadMessages` | local `Message` entity (`IsRead` flag, `SenderId`/`ReceiverId`) | real 1:1 messaging already exists locally — `BuildDashboardUnreadMessagesAsync` |
| `active_package` | dropped entirely | no local equivalent, and unrendered by any gamatrain-front component even in Phase 0 |
| `section`/`course` | `ApplicationUser.Board`/`Grade` → `board`/`grade` | not the same scale as gama-api's opaque codes, but the same underlying curriculum-board/grade-level concept |
| `area` | dropped entirely | no local equivalent, replaced rather than kept |
| `score_check_info` | **unchanged — still gama-api's raw value** | no local equivalent exists at all; kept as-is, not nulled |
| `stats` (test/file/question published counts) | **unchanged — still gama-api's raw values** | no local content domain (PastPaper/Multimedia/Forum) exists yet; `Question` is an exam-bank item, not a forum post |
| `examSuggestions` | **unchanged — still gama-api's raw values** | tied to gama-api's own exam-suggestion engine, no local equivalent |

`IdentityService.GetDashboardAsync` builds the local pieces first (`BuildDashboardUserAsync`,
`BuildDashboardProfileCompletionAsync`, `BuildDashboardUnreadMessagesAsync` — always run,
independent of gama-api), then merges in whatever gama-api still contributes
(`LegacyDashboardDataDto` — `Stats`/`ExamSuggestions`/`ScoreCheckInfo`)
onto that same `User` object. `LegacyDataAvailable`/`LegacyAuthRejected` now govern only that
legacy-sourced remainder, not the whole response — `User`/`ProfileCompletion`/`UnreadMessages` are
present and correct even when gama-api is completely unreachable.

The legacy-proxying mechanism itself (still needed for the fields above that have no local
equivalent) is otherwise unchanged from Phase 0:
- **Server picks teacher vs. student — preferring the legacy JWT's own `group_id` claim over the
  local column.** `IdentityService.GetDashboardAsync` calls gama-api's teacher or student endpoint
  accordingly (`Core:TeacherDashboard` / `Core:StudentDashboard`), mirroring gamatrain-front's own
  `userType === 5 ? teachers : students` ternary exactly (5 = Teacher; anything else, including
  `null`, falls through to the student endpoint) — this proxy changes no behaviour for any caller,
  and the frontend no longer needs to make that choice itself. The value compared against `5` is
  `GetLegacyJwtGroupAsync(token)` (decodes/verifies the same incoming legacy JWT via the shared
  `ValidateLegacyJwtAsync` helper and reads its `group_id` claim) **falling back to local
  `ApplicationUser.Group` only if that decode fails**. **Bug fixed 2026-09-01, found via live
  testing with a real gama-api session**: this used to trust local `Group` alone, which is only as
  fresh as the caller's last legacy login or the one-time backfill - confirmed live, a teacher whose
  local `Group` was `NULL` got routed to `/students/dashboard`, which gama-api correctly 403's, and
  the next bullet's `LegacyAuthRejected` then misread that 403 as "your session is invalid",
  hard-401ing a caller whose token and session were both perfectly fine. The JWT's own claim is
  gama-api's live, authoritative answer for the exact session being forwarded, so it can't go stale
  the way the local column can.
- **Graceful degrade, never a hard failure.** Not every caller has a forwardable legacy token (a
  native/local-token account has none), and gama-api itself can be unreachable or error. Either
  case sets `DashboardResponseDto.LegacyDataAvailable = false` with every other field left `null` -
  the endpoint still returns `succeeded: true`, so the frontend can render an empty/skeleton state
  for the affected widgets instead of erroring the whole dashboard. This mirrors the "never throw to
  the caller" convention (`docs/development/coding-standards.md`) at the granularity of one external
  dependency, not the whole request.
  `IdentityService.GetDashboardAsync` must tell "no legacy token" apart from "a token, but not a
  legacy-JWT-shaped one" *before* ever calling `CoreProvider` - it reuses the same `|`-split check
  `TokenAuthenticationHandler` uses (`Constants.DelimiterAlternate`, see "Presenting a token" above)
  to detect a local opaque `{userId}|{token}` token and skip the gama-api call entirely for those.
  **Bug fixed 2026-09-01, found via live local testing**: an earlier revision only checked
  `string.IsNullOrEmpty(token)`, so a native account's own (non-empty, just wrong-shaped) token was
  forwarded to gama-api as-is, which correctly rejected it as garbage with 401/403 - and that then
  hit the *next* bullet's real-401 path, hard-failing this endpoint for every native-account caller
  instead of degrading. If you touch this method again, keep the shape check ahead of the gama-api
  call, not just an emptiness check.
- **One exception: a 401/403 from gama-api is a real HTTP 401, not a quiet degrade.** If gama-api
  rejects the caller's forwarded legacy token with 401/403, `CoreProvider.GetDashboardAsync` sets
  `DashboardResponseDto.LegacyAuthRejected = true` and `IdentitiesController.GetDashboard` returns
  an actual `401 Unauthorized` (`ApiControllerBase.Unauthorized<T>` /
  `UnauthorizedObjectResult<T>`), *not* the usual `200` + `succeeded: true` degrade above. This
  case is meaningfully different from "gama-api is unreachable/erroring": this backend's own auth
  already accepted the same token as valid (correct signature, not expired), but gama-api itself no
  longer honors it - e.g. the session was ended via gama-api's own logout, or the account was
  disabled, directly on gama-api's side, independent of anything this backend's own token
  validation checks. gamatrain-front's global response interceptor (`useApiService.ts`'s
  `onResponseError`) already redirects to login on any `401`/`403` from any endpoint, so returning a
  genuine `401` here (once, deliberately) reuses that existing mechanism to force
  re-authentication - instead of the caller silently continuing to use a session this backend
  believes is fine while gama-api no longer honors it. This is a **deliberate, narrowly scoped
  exception** to this API's otherwise-universal "always 200, check `succeeded`/`errors` in the body"
  convention (see `CLAUDE.md`) - checked as of 2026-09-01, no other gama-api proxy in this codebase
  (`download`, `legacy-auth/group`, etc.) does this; they all still collapse *every* gama-api
  failure, 401/403 included, into `succeeded: false` with an outer `200`. Don't copy this pattern to
  another endpoint without the same reasoning applying - it was deliberately not generalized in the
  same change.
- **The subscription banner now has real data** (`User.Subscription`, added in Phase 2 - see the
  table above) - the frontend still needs its own change to actually render it instead of the
  static "Coming soon" placeholder (`app/components/user/dashboard/subscriptionBanner.vue`), which
  is out of scope for this backend change.
- **The achievements/badges strip still has no real data source anywhere** and remains entirely out
  of scope for this endpoint, not merely deferred - no badge domain exists in *either* backend; it
  would need new entities and earning rules, i.e. its own separate feature.

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
backfill role assignment for users who predate it; that's a separate one-time operation, see
"One-time backfill for existing users" below.

`CoreProvider.cs` reads this off gama-api's own response via `info?.Group.ValueOf<int?>()` — on
gama-api's side it's apparently a real enum/smart-enum type; this app only ever sees and stores
the flattened raw integer, never gama-api's own type definition, which is why this repo has no
local named constants for it beyond the confirmed `5`/`6`.

**New teacher accounts default to a Public profile** (added 2026-08-22,
`IdentityService.DefaultTeacherProfileToPublicAsync`, called right after `SyncRoleFromGroupAsync`
in the "create new user" branch of `SyncLegacyAuthAsync`). Every account otherwise starts with
`ProfileVisibility.Private` (see `RegisterAsync`/`SyncLegacyAuthAsync`), which means it's excluded
by default from `GET identities/profiles/list` (hard-filtered to `ProfileVisibility.Public` —
`IdentitiesController.GetPublicProfile`). For a brand-new user who was just assigned `Role.Teacher`
by the role-sync above, this flips the starting value to `Public` instead, so new teachers are
discoverable in that listing out of the box. Deliberately scoped narrowly:

- Keyed off the actual `Role.Teacher` membership (re-checked via `IsInRoleAsync` after the role
  sync call), not `Group` directly — it only fires if that role sync actually succeeded.
- **New accounts only.** Never re-applied on a later login, `Group`/role change, or via the
  `legacy-auth/group` proxy — an existing user's own `ProfileVisibility` choice (via
  `ManageProfileSettingsAsync`) or an existing account's current setting is never overwritten. A
  teacher who already existed before this shipped, or whose account predates being assigned
  `Role.Teacher`, keeps whatever `ProfileVisibility` they already have; a teacher can still switch
  back to `Private` any time via `ManageProfileSettingsAsync`, same as any other user.
- Best-effort, same as `SyncRoleFromGroupAsync`: a failure here is logged and swallowed, never
  fails the login itself.

### One-time backfill for existing users (removed 2026-09-03)

Both mechanisms above only ever act going forward (a legacy login, or a brand-new account) — by
design, neither touches a user who already existed before they shipped. `IdentityService.
BackfillRoleAndProfileVisibilityFromGroupAsync` (added 2026-08-22) was the explicit, separate,
one-time catch-up for the rest of the user base:

- For every existing user with `Group = 5` or `6`, applied the same role sync
  (`SyncRoleFromGroupAsync`) a legacy login would apply.
- For every existing `Group = 5` (Teacher) user, **unconditionally** set `ProfileVisibility =
  Public` — unlike the new-accounts-only default above, this one deliberately overwrote whatever a
  user currently had, including a value they may have deliberately chosen themselves. There's no
  field in the data that distinguishes "explicit choice" from "never touched, still on the created
  default" — running this backfill was a deliberate decision to prioritize making existing teachers
  discoverable over preserving that ambiguity.
- Triggered via `POST admin/{v}/identities/backfill-teacher-student-roles`, enqueued as a Hangfire
  background job — same reasoning as the avatar-conversion backfill before it: this table is tens
  of thousands of rows, well past any realistic HTTP/proxy timeout if run inline.

**Removed** along with its admin endpoint, service method, and result DTO once it had been run to
completion in production, following the same pattern as the avatar-conversion backfill's own
removal — a one-time-style backfill isn't meant to stay callable indefinitely once its job is done.

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
