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

### Candidate pool and `AllNudgesCompletedAt`

Before evaluating any `NudgeType`, `EvaluateAndSendNudgesAsync` builds one candidate pool:
`RegistrationDate` at least **7 days** ago, a non-null `Email`, `AllNudgesCompletedAt == null`, and
`NudgesOptedOutAt == null` (see "Unsubscribing" below). **Added 2026-09-02, before this ever ran
against real data**: without the `AllNudgesCompletedAt` filter, every run would re-scan the *entire*
eligible user population against all six per-field text-column conditions, forever — including
users who completed their profile years ago and will never match again. `ApplicationUser
.AllNudgesCompletedAt` is a one-way latch: the first time a user has zero remaining gaps across
every currently-defined `NudgeType`, it's set, and the candidate-pool query excludes them via one
indexed null-check from then on, without ever re-deriving completeness from the live columns again.

**Deliberately not self-correcting**: if a field this depends on is ever cleared again after being
set (an admin edit, a future data migration, a `NudgeType` being re-enabled after a user was
already latched while it was disabled), this column does not detect that — the user simply stops
being evaluated for anything, permanently. Accepted deliberately: a cheap fix for a real, growing
cost (this app has ~30k production users as of 2026-09-02), not worth automatic re-detection for how
rare that case is. Distinct from the dashboard's own, always-freshly-computed completeness
(`IdentityService.BuildDashboardProfileCompletionAsync`, `docs/business/identity-and-access.md`) —
that one includes `CurrentStatusSentence` (no `NudgeType` covers it) and excludes `Group` (which
`RoleMissing` does cover); the two are related concepts, not the same signal, and the dashboard
never reads this column.

### Eligibility, cooldown, and send cap

`NudgeService.EvaluateAndSendNudgesAsync` iterates `NudgeType`s in a fixed order (`AllNudgeTypes` —
`RoleMissing, AvatarMissing, NameMissing, BioMissing, SkillsMissing, ExperienceMissing`), acting as
an implicit priority when a user qualifies for more than one. For each type with an `Active`
`NudgeTemplate`:

1. Within the candidate pool above, checks the type's own condition (table above) — re-checked on
   every run, so a user who resolves the condition between runs (e.g. sets their avatar) is simply
   no longer selected and never gets nudged for it again. This check always runs for every enabled
   type, every run, regardless of the send cap below (point 4) — skipping it once the cap is hit
   would let a user whose only remaining gap is a type this run never got to wrongly get latched as
   `AllNudgesCompletedAt`.
2. Excludes anyone with a `UserNudgeLog` row for that specific `(UserId, NudgeType)` where
   `SendCount >= 3` (max sends of that exact type, ever) or `LastSentDate` is within the last
   **14 days** (same-type resend cooldown).
3. **Excludes anyone nudged at all (any `NudgeType`) within the last 7 days** —
   `MinDaysBetweenAnyNudge`, checked globally, not per type. **Bug fixed 2026-09-02, found live in
   sandbox**: without this, a user eligible for several `NudgeType`s at once (a long-registered
   account that never completed *any* profile field — exactly the oldest accounts, since they've
   had the most time to accumulate missing fields without ever filling one in) got one email per
   type, all in the same run — read as spam. The exclusion set starts from `UserNudgeLog`'s most
   recent send per user across every type, then grows live as sends happen within the same run —
   so it also naturally caps a single run to at most one send per user (a second send 0 days later
   would violate the same 7-day floor), with no separate "already sent this run" mechanism needed.
   Whichever type a user doesn't get nudged for today waits for a later run, still subject to its
   own per-type cooldown/cap (point 2) once it does fire.
4. **`MaxSendsPerRun = 100`** — a hard cap on total emails actually sent in one run, across every
   `NudgeType` combined. **Added 2026-09-02, before first production run**: with ~30k existing
   users, a first run (or any run with a large backlog) could otherwise try to send thousands of
   emails in one job execution. Reuses the same number `ResendEmailProvider` already chunks
   recipient lists at (`Chunk(100)`, Resend's own per-call limit) for consistency, though the
   mechanism differs — each nudge send is already its own single-recipient call, so this bounds the
   *count* of sequential calls a run makes, not a batch size. Once hit, the run stops sending
   entirely for the rest of that run (point 1's completeness check keeps running regardless, see
   above); anyone not reached is picked up on a later run, since none of their state
   (`UserNudgeLog`, `AllNudgesCompletedAt`) changes until they're actually sent to.
5. Sends via `IEmailService.SendEmailAsync` (body always gets an unsubscribe footer appended, see
   below — not dependent on the template author remembering a placeholder), then upserts the
   `UserNudgeLog` row (`SendCount++`, `LastSentDate = now`). Sent `From = EmailService
   .GetNoReplyEmail()` (`Gamatrain <noreply@gamatrain.com>`) — explicitly set, same convention as
   `SubscriptionService`/`IdentityService`'s other automated/system emails. **Fixed 2026-09-03**:
   originally left unset, which made `EmailService.SendEmailAsync` fall back to
   `GetSupportEmail()` — an automated, cyclical, up-to-3-times-per-type email arriving from the
   support inbox reads as a person emailing you, and any reply lands in the support queue with
   nothing there set up to handle it.

Net effect: a user missing every profile field gets nudged about **one field at a time**, at most
once every 7 days, cycling through `RoleMissing → AvatarMissing → … → ExperienceMissing` one per
run — never a burst of several emails in one night, and never more than 100 emails total leave this
app in one run regardless of how large the backlog is.

Never fails the whole run for one bad email/user - `NudgeService`'s own outer `try/catch` covers
the method as a whole (matching the "never throw to the caller" convention), so a single failure
during evaluation surfaces as a `Failed` `ResultData` from the job rather than crashing Hangfire;
per-user send failures are not currently individually isolated (a future hardening item, not done
in this first pass - `UpdateOrphanUsersAsync`'s per-user isolation, see
`PROJECT_SNAPSHOT.md`'s 2026-08-22 entry, is the pattern to follow if this needs it later).

### Unsubscribing

Two paths, both landing on `ApplicationUser.NudgesOptedOutAt` (null = subscribed; set = opted out).
Unlike `AllNudgesCompletedAt`, this one is meant to be reversible.

- **`GET api/v1/nudges/unsubscribe?userId=&token=`** (`NudgesController`, anonymous) — the link every
  nudge email carries, appended to the body by `NudgeService.BuildUnsubscribeFooter` regardless of
  what the admin wrote in the template (never relies on a `[UNSUBSCRIBE_URL]`-style placeholder
  being remembered). No login required — the token itself is the credential, since a viewer clicking
  a link from their inbox isn't expected to also be signed in. The token is minted with
  `IDataProtectionProvider.CreateProtector("NudgeUnsubscribe").Protect(userId)` and verified the same
  way on click — deliberately **not** Identity's `UserManager` token provider
  (`ApiDataProtectorTokenProviderOptions.TokenLifespan`, 10 days by default, is one setting shared by
  every purpose that provider mints for — wrong for a link that must still work whenever an unread
  email finally gets opened, weeks later). This token never expires by design. `CtaUrl`-style base:
  `Notifications:UnsubscribeBaseUrl` config (`https://gamatrain.com/unsubscribe`) — the frontend page
  at that route (not yet built) is expected to call this endpoint and show a confirmation; the
  backend endpoint itself works today independent of that.
- **`PUT api/v1/nudges/subscription?subscribed={bool}`** (`NudgesController`, `[Permission(policy:
  null)]`) — authenticated toggle for the caller's own subscription. The counterpart the one-way
  email link can't offer: a logged-in user opting back in (`subscribed=true` clears
  `NudgesOptedOutAt`).

Opted-out users are excluded from the candidate pool entirely (same as `AllNudgesCompletedAt`) — no
`NudgeType` is ever evaluated for them until they resubscribe.

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
