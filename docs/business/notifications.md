# Notifications

## Nudge system

Business logic: `src/Application/Service/NudgeService.cs`, contract:
`src/Application/Interface/INudgeService.cs`. Entities: `src/Domain/Entity/NudgeTemplate.cs`,
`src/Domain/Entity/UserNudgeLog.cs`. Smart enum: `src/Domain/Enumeration/NudgeType.cs`. Admin CRUD:
`src/Presentation/Api/Areas/Admin/Controllers/NudgesController.cs` (`api/v1/admin/nudges`).

A **proactive, scheduled** email nudge system — "you haven't done X yet, here's a reminder" —
evaluated daily by a Hangfire `RecurringJob` (`EvaluateAndSendNudges`, `Startup.cs`,
`Cron.Daily(1, 0)`), not triggered by a single user action. First use case: profile-completion
prompts (added 2026-09-02). Deliberately designed to be reused for future, unrelated invite types
(e.g. "invite a teacher to create an exam") without re-architecting — adding a new nudge means
adding a `NudgeType` value, its eligibility check (`NudgeService.ApplyEligibilityFilter`), and its
`NudgeTemplate` row (admin-editable, no deploy needed for that part).

### Deliberately separate from `ApplicationSettingsDto`'s email templates

`ApplicationSettingsDto` already holds ~19 flat string properties for **reactive/transactional**
emails (ticket confirmations, contribution confirmations, subscription lifecycle,
account-deletion) — each fires once, immediately, off one specific action. The nudge system is a
genuinely different kind of thing: a real entity (`NudgeTemplate`), evaluated on a recurring
schedule, that can resend up to a cap. Forcing both into one shape would either bloat the simple 19
with unused cooldown/CTA fields, or bloat the nudge system with concepts it doesn't need. **Do not
merge these two systems** — this was discussed and deliberately rejected; see git history
(2026-09-02) if revisiting.

### Schema

- **`NudgeTemplate`** (`NudgeTemplates` table, admin-managed via `NudgesController`): `Id`,
  `NudgeType` (unique), `Subject`, `Body` (placeholders `[RECEIVER_NAME]`, `[CTA_URL]`),
  `CtaLabel`, `CtaUrl`, `IsActive`, `CreationDate`. Turning `IsActive` off stops that nudge type
  entirely (skipped by `EvaluateAndSendNudgesAsync`) without a deploy.
- **`UserNudgeLog`** (`UserNudgeLogs` table, internal — no CRUD endpoint): `Id`, `UserId`,
  `NudgeType`, `LastSentDate`, `SendCount`. Unique on `(UserId, NudgeType)`. This is what makes
  repeated runs safe.

### `NudgeType` values (first batch, profile-completion)

`RoleMissing`, `AvatarMissing`, `NameMissing`, `BioMissing`, `SkillsMissing`, `ExperienceMissing`.

Deliberately **not** included in this batch: a school-photo nudge. Every type above is "one profile
field, set or not" — a clean completion state. School-photo contribution is "contribute to *any*
school," not a single yes/no state on the user's own profile, so it doesn't have a natural
"missing" trigger or a single CTA URL the way the others do. Left for a later, separate nudge type
once a genuine trigger shape for it (e.g. zero `SchoolImage` contributions after N days) is
designed - not scoped in this pass.

Eligibility per type (`NudgeService.ApplyEligibilityFilter`) deliberately matches the same
underlying signals `UserRateLevel.Calculate` and `IdentityService.BuildDashboardProfileCompletionAsync`
already use (`docs/business/identity-and-access.md`, "User dashboard proxy") — including the same
length thresholds (bio > 49 chars, etc.) — so a field counted "complete" on the dashboard is never
still nudged for here, and vice versa:

| NudgeType | Condition |
|---|---|
| `RoleMissing` | `ApplicationUser.Group == null` |
| `AvatarMissing` | `AvatarId` empty |
| `NameMissing` | `FirstName` or `LastName` empty |
| `BioMissing` | `Biography` empty or ≤ 49 chars |
| `SkillsMissing` | `Skills` empty |
| `ExperienceMissing` | zero `Experience` rows |

### Eligibility, cooldown, and send cap

`NudgeService.EvaluateAndSendNudgesAsync`, for each `NudgeType` with an `Active` `NudgeTemplate`:

1. Candidate users: `RegistrationDate` at least **7 days** ago, a non-null `Email`, and the
   type's own condition (table above) still true — re-checked on every run, so a user who resolves
   the condition between runs (e.g. sets their avatar) is simply no longer selected and never gets
   nudged for it again.
2. Excludes anyone with a `UserNudgeLog` row for that `(UserId, NudgeType)` where `SendCount >= 3`
   (max sends, ever) or `LastSentDate` is within the last **14 days** (resend cooldown) — i.e. up to
   3 sends total, at least 2 weeks apart.
3. Sends via `IEmailService.SendEmailAsync`, then upserts the `UserNudgeLog` row (`SendCount++`,
   `LastSentDate = now`).

Never fails the whole run for one bad email/user - `NudgeService`'s own outer `try/catch` covers
the method as a whole (matching the "never throw to the caller" convention), so a single failure
during evaluation surfaces as a `Failed` `ResultData` from the job rather than crashing Hangfire;
per-user send failures are not currently individually isolated (a future hardening item, not done
in this first pass - `UpdateOrphanUsersAsync`'s per-user isolation, see
`PROJECT_SNAPSHOT.md`'s 2026-08-22 entry, is the pattern to follow if this needs it later).

### Admin endpoints (`api/v1/admin/nudges`, `[Permission(Roles = [nameof(Role.Admin)])]`)

| Verb | Route | Purpose |
|---|---|---|
| GET | `templates` | List all `NudgeTemplate`s (paged) |
| GET | `templates/{id}` | Get one |
| POST | `templates` | Create |
| PUT | `templates/{id}` | Update (partial - only non-null fields overwrite) |
| DELETE | `templates/{id}` | Remove |

### Seed data

The `AddNudgeSystem` migration seeds all 6 `NudgeType`s with a default, `Active` template pointing
at `https://gamatrain.com/user/type` (role) or `https://gamatrain.com/user/profile` (the other
five) - the feature works immediately after deploy without requiring manual admin setup first, but
every seeded template's copy/links can be edited or deactivated via the CRUD endpoints above like
any other row.
