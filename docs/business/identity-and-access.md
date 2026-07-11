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
