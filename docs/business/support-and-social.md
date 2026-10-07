# Support & Social

Business logic: `src/Application/Service/TicketService.cs`,
`ReactionService.cs`, `ConnectionService.cs`, `MessageService.cs`.
Entities: `src/Domain/Entity/Ticket.cs`, `TicketReply.cs`, `Reaction.cs`,
`Connection.cs`, `Message.cs`, `LoginHistory.cs`.

## Support tickets

`Ticket` (`Ticket.cs`) supports both authenticated and anonymous
submitters (`UserId` is optional, `FullName`/`Email` are always captured).
`CreateTicketAsync` (`TicketService.cs:134-172`) is reachable from an
`[AllowAnonymous]` controller action that requires captcha verification
(`Presentation/Api/Controllers/TicketsController.cs:168-190`) and only sets
`UserId` if the caller is authenticated.

For an authenticated caller the ticket's `Email` is taken from the account
(the `ClaimTypes.Email` claim, rebuilt from the DB on each request) and the
posted `Email` is ignored — otherwise a user could file a ticket under someone
else's address and send them the confirmation/reply mails.

**Which tickets are "mine" (2026-10-05):** `UserTicketsSpecification` /
`UserTicketReplysSpecification` match a ticket by `UserId`, **or** by `Email`
only when the account's email is confirmed (the `EmailConfirmed` claim, read
through `ClaimsPrincipal.ConfirmedEmail()`). Public `identities/register`
doesn't confirm the address and lets the user sign in anyway, so before this
anyone could register with an address that had no account and read every
anonymous or emailed ticket from it, replies included. Tickets filed while
signed in, and inbound emails from an address that already has an account,
carry `UserId` and are unaffected; what an unconfirmed account no longer sees
is tickets sent from its address *before* it signed up. Only admin-created
accounts are confirmed today (no user-facing email confirmation flow exists). The posted `Email` is only used by anonymous callers
and by accounts with no email on file (phone-only sign-ups); if neither exists
the request fails with a required-field error. The frontend can tell which case
it is in from the `email` field of `GET /api/v2/identities/profiles`.

There is **no formal status/priority workflow** — no "open/closed" or
priority/category field exists. State is tracked purely through boolean
read flags: `Ticket.IsReadByAdmin` and `TicketReply.IsRead`/`IsReadByAdmin`.
An admin opening a ticket (`GET admin/tickets/{id}`) marks it read through
`SetAsReadByAdminAsync`; only the explicit `PATCH admin/tickets/{id}/toggle`
flips it either way (`ToggleIsReadByAdminAsync`). Opening a reply thread marks
its unread replies read (`SetReplysAsReadedByUserAsync`/`ByAdminAsync`, which
only touch rows still unread). Replying sets the reply's flags inversely
depending on who replied (`ReplyTicketAsync`) — a user reply marks it
read-by-user/unread-by-admin and vice versa, and an admin reply emails the
reply to the ticket's `Email`.

**Fixed 2026-10-05:** both ticket-details endpoints called
`ToggleIsReadByAdminAsync` on every view. So a customer opening their own
ticket flipped the admin's read state, and an admin opening an already-read
ticket marked it *unread* again. The admin view now only sets it to read, and
the customer view doesn't touch it.

**Bug fixed 2026-09-09, live-reported as "admin doesn't notice a new email
arrived on a ticket":** `ReplyTicketAsync` had set *both* `IsRead` and
`IsReadByAdmin` to the exact same expression (`!requestDto.ReplyByAdmin`),
instead of opposite ones — a fresh customer/inbound-email reply was stamped
`IsReadByAdmin = true`, i.e. created already marked as read by admin. That
silently broke the "has an unread reply" signal `GetTicketsAsync`'s sort
depends on to surface new correspondence. Compounding it, that same sort's
`ThenBy(t => t.TicketReplys.Any(r => !r.IsReadByAdmin))` sorted *ascending* -
even with the flag fixed, a ticket with a genuine unread reply would sort to
the *bottom* of its `IsReadByAdmin` bucket instead of the top (`GetUserTicketsAsync`'s
customer-facing equivalent had the identical direction bug on its own
`OrderBy(t => t.TicketReplys.Any(r => !r.IsRead))`). And on top of both of
those, nothing reset the *ticket's own* `IsReadByAdmin` back to `false` when
a new non-admin reply came in - only the admin's own `ToggleIsReadByAdminAsync`
action touched it - so a ticket an admin had already opened once stayed in
the "already read" bucket indefinitely, sorted only by its original
`CreationDate`, regardless of new replies. All three are now fixed:
`IsReadByAdmin = requestDto.ReplyByAdmin` (opposite of `IsRead`, not the same
expression); both sorts changed to `ThenByDescending`/`OrderByDescending`;
and `ReplyTicketAsync` now resets `Ticket.IsReadByAdmin` to `false` whenever
`!requestDto.ReplyByAdmin`.

**Admin list ordering by latest activity (2026-10-05):** the admin sort's
second key used to be "has an unread reply", then `CreationDate` - so
among tickets with new replies, order followed when the *ticket* was opened,
not when the reply arrived (an old ticket replied to minutes ago could sit
below a newer one replied to days ago). `Ticket.LastActivityDate` is now a
stored column: set on create, moved forward by every reply (admin or customer)
in `ReplyTicketAsync`, and backfilled by the `TicketLastActivityDate`
migration (newest reply date, else `CreationDate`). `GetTicketsAsync` ordered by
(until 2026-10-06, see "no unread-first" below) `IsReadByAdmin`, then `LastActivityDate` desc, served by the
`IX_Tickets_IsReadByAdmin_LastActivityDate` index, declared
`(IsReadByAdmin ASC, LastActivityDate DESC)` to match the sort (an all-ascending
index can't serve mixed directions) - no per-row aggregation over replies.
`ReplyTicketAsync` loads the ticket tracked and saves the new reply and the
ticket's `LastActivityDate`/`IsReadByAdmin` in one `SaveChanges`, so they can't
diverge. A reply to a missing ticket returns `NotFound`. The admin list
response also carries `HasNewReply` (an unread-by-admin reply exists) and
`LastActivityDate`, so the panel can tell a brand-new ticket from a new reply
on an existing one. `GetUserTicketsAsync` (customer side) keeps "has a reply the
customer hasn't read" first, then sorts by `LastActivityDate` instead of
`CreationDate`.

**The sort above never actually applied until the follow-up fix (2026-10-05,
found in production right after #724 shipped):** both list methods order the
query and then call `FilterListAsync`, which always re-ordered it, by the
request's `SortFilter` or by default `Id desc`. A second `OrderBy` replaces the
first, so `/admin/contact-us` was really sorted by ticket id: newest *created*
first, and a reply on an older ticket never brought it to the top. This was
true before #724 as well. Now `FilterListAsync` keeps an order the query
already has when the request has no `SortFilter`, and both ticket lists drop
any client `SortFilter` (`TicketService.WithoutSort`) so their business order
always applies. Paging and search filters still work.

**Admin list: no unread-first, plus filters (2026-10-06):** sorting unread
tickets first meant opening a ticket (which marks it read) moved it below every
unread one, often onto another page - an admin who left to look something up
(e.g. the payments list) and came back couldn't find the ticket they were
answering. The admin list is now ordered by `LastActivityDate` desc only (then
`Id` desc), served by `IX_Tickets_LastActivityDate`; the bold row /
`HasNewReply` still mark what's unread. The "unread only" view is a filter
instead: `GET admin/tickets` takes `unread` (`TicketUnreadByAdminSpecification`:
the ticket or any reply unread by admin, same meaning as `HasNewReply`; `false`
negates it, i.e. the ticket and every reply are read; omitted = no filter),
`search` (`TicketSearchSpecification`: contains on subject, name or email; a
term that parses as a ticket number - `123`, `#123`, `Ticket-123`,
`[Ticket-123]` as quoted from our email subjects - also matches the id),
`email` (exact sender: every ticket from one customer) and
`startDate`/`endDate` (inclusive, on `LastActivityDate`). Filters combine with
AND. The customer list keeps its own order (unread reply first).

**Other ticket fixes (2026-10-05):**
- *Customer reply ownership.* `POST tickets/{id}/replys` didn't check that the
  ticket was the caller's, so any signed-in user could post into anyone's thread
  (IDs are sequential). The controller now checks
  `ExistsTicketAsync(IdEquals(id) && UserTicketsSpecification(User))` first.
  `ReplyTicketAsync` itself still doesn't check ownership, because the admin area
  replies to any ticket.
- *Confirmation email.* `SendTicketConfirmationAsync` put the sender's raw body
  and name into the HTML template. The anonymous form picks the recipient, and an
  inbound email's `From` can be forged, so this let anyone have our domain mail
  their HTML to any address. The body now goes through `SanitizeHtml()` and the
  name through `SanitizePlainText()`.
- *Inbound sender match.* An inbound `[Ticket-N]` email is added to the ticket
  only when the parsed `From` address (`MailAddress`) equals the ticket's
  `Email`. This used to be a substring check, so `victim@x.com.evil.io` passed. New
  tickets from email also store the bare address now, not `"Name" <addr>`.
- *Inbound attachments.* `ResendEmailProvider` downloaded every attachment, and
  `TicketService` then dropped them all. Now the first attachment that passes the
  web upload's checks (image/zip extension, at most 2 MB) is stored on the new
  ticket or reply. A ticket or reply holds one file, so the others are logged and
  skipped.
- *Delete.* `RemoveTicketAsync` didn't await its `SaveChangesAsync`, so a failed
  delete went unnoticed while the file was removed anyway. It now awaits the delete
  and then removes the ticket's file and every reply's file. The reply rows go by
  cascade, but their files didn't.
- *Reply order.* `GetTicketReplysAsync` had no `ORDER BY`. Replies are now
  returned oldest first (`CreationDate`, then `Id`).

`ProccessInboundEmailAsync` (`:397-460`) supports replying to tickets by
email: it matches inbound messages to an existing ticket by a
`[Ticket-N]`-style subject pattern (regex at `:482`) and appends them as
replies, or creates a new ticket if no match is found. The inbound content
itself comes from `ResendEmailProvider.ProccessInboundEmailAsync`
(`Infrastructure/Provider/Email/ResendEmailProvider.cs`), which retrieves
the received email from Resend's API and takes its `HtmlBody` (falling
back to `TextBody` only when the sender's email had no HTML part) — fixed
2026-07-14; it previously always took `TextBody`, which for an
HTML-composed inbound email is Resend's auto-generated degraded plain-text
rendering (`<img>`/`<a>` tags flattened to `[url]text`), not the real
message, so every inbound HTML email arrived in the ticket system already
mangled.

**Silent inbound-email loss, partially fixed 2026-09-09 (logging only -
see below for the still-open part):** live-reported as "some inbound emails
never show up on our side, even though Resend's webhook fires correctly."
Resend's `email.received` webhook payload is metadata-only (`from`/`to`/
`subject`/`message_id`/attachment list - no body), so
`ProccessInboundEmailAsync` must make a follow-up live call back to
Resend's API (`ReceivedEmailRetrieveAsync`, plus one more per attachment)
just to get the content - each one a point of failure with no retry of its
own. Before this fix, every failure branch in that method (missing
`svix-*` header, bad/expired signature, wrong event type, a Resend API call
throwing) returned a `Failed`/no-op result with **no logging at all** for
most of them; the caller, `TicketService.ProccessInboundEmailAsync`, then
discarded that result with no logging of its own; and
`TicketsController.InboundWebHook` always answers Resend/Svix with `200`
regardless of outcome (deliberately, same convention as
`PaymentsController.RecurringWebhook` for Stripe - the gateway/provider
only reads the HTTP status, and a non-200 would make it retry a
permanently-bad event like an invalid signature forever), so Resend never
even knows to retry. Net effect: a dropped inbound email - first message or
reply - left zero trace anywhere it could be diagnosed from. Fixed so far:
every failure branch in `ResendEmailProvider.ProccessInboundEmailAsync` now
logs its specific cause (`LogWarning`, including the `svix-id` for
cross-referencing against Resend's own dashboard), and
`TicketService.ProccessInboundEmailAsync` now logs a warning with the
underlying errors whenever the result isn't `Succeeded`, instead of
silently returning.

**Still open:** the fix above only makes a drop *visible* - it does not make
a *transient* one (a Resend API hiccup fetching the content/attachments, a
brief network blip) recoverable. That failure shape gets exactly the same
"200, no retry" treatment as a permanently-bad signature, even though -
unlike a bad signature - retrying it would very likely succeed. Also
production file logging (`Serilog`, `logs/log_.log`) was itself broken for
at least 5+ days before 2026-09-09 due to a directory-ownership mismatch on
the VPS (see `docs/deployment/overview.md`) - any drops during that window
left no trace even with this fix in place, since the fix only adds logging,
it doesn't recover already-lost history.

**Access scoping** is enforced at the controller layer, not inside the
service: end-user endpoints filter by `UserTicketsSpecification(User)` /
`UserTicketReplysSpecification(id, User)`
(`TicketsController.cs:38-42, 74-75, 114, 146-157`), while the Admin-area
controller (`Areas/Admin/Controllers/TicketsController.cs:26`,
role-gated to `Role.Admin`) has no per-ticket ownership filter — any admin
can see/act on any ticket, which is the intended design for a support
inbox.

## Reactions

`Reaction` (`Reaction.cs`) is a simple like/dislike boolean (`IsLike`), not
a multi-value reaction type or star rating. It attaches to an item via
`CategoryType` + `IdentifierId`, with a unique index on
`(CategoryType, IdentifierId, CreationUserId)` enforcing one reaction per
user per item (`Reaction.cs`, DB table "Reactions"). The `CategoryType`
values that reactions can attach to
(`src/Domain/Enumeration/CategoryType.cs:9-27`) are: `School`,
`SchoolComment`, `SchoolImage`, `Post`, `SchoolIssues`,
`RemoveSchoolImage`, `PostComment` — i.e. reactions cover schools, school
photos/comments/issues, and posts/comments. `ManageReactionAsync`
(`ReactionService.cs:59-105`) looks up any existing reaction by the same
key; submitting the same `IsLike` value again is rejected as a duplicate
(`:75`), otherwise it flips or inserts.

Note: the separate `ItemType` enum (`src/Domain/Enumeration/ItemType.cs:9-15`:
`School`, `Post`, `Profile`) is unrelated to Reactions — it's used only for
sitemap generation.

## Connections (follow/unfollow) & Messages

`ConnectionService.cs` implements a **follow model**, not classic mutual
friend requests. `FollowAsync` (`:111-152`) creates a `Connection` with
`Status = Requested`, blocking duplicate requests if a `Confirmed` or
already-`Requested` row exists. Confirming a follow request
(`ConfirmFollowRequestAsync`, `:253-283`) flips the *original* request to
`Confirmed`; if `TwoWay` is set, it additionally inserts a **new**, separate
`Connection` row with `Status = Confirmed` for the reverse direction. (**Fixed
2026-07-11** — it previously set the original request to `Rejected` instead
of `Confirmed`, so confirming a follow request silently rejected it while
reporting success; only the `TwoWay` reverse-row insert worked as intended.)
`UnFollowAsync` (`:154-187`) sets status to `Revoked` (optionally both
directions). Status values (`src/Domain/Enumeration/ConnectionStatus.cs:9-21`):
`Requested`, `Confirmed`, `Rejected`, `Canceled`, `Revoked`.

**Target-user resolution by `CoreId`.** The `users/{id}/...` actions
(`followers`/`followings`/`follow`/`unfollow`/`subscriptions/toggle`) accept
an optional `idType` query parameter (`IdentifierType.Id` default or
`.CoreId`) so a caller that only knows a user's legacy gama-api `CoreId`
(e.g. a pastpaper author, sourced from the old backend) doesn't need a
separate lookup step first —
`IIdentityService.ResolveUserIdAsync`/`ResolveUserIdsAsync` resolve it
against `ApplicationUser.CoreId` before the normal connection logic runs. An
unlinked `CoreId` returns a `UserNotFound` error; it is never used to
auto-create a local user. `POST connections/status` is the bulk counterpart
— given a list of ids (all `Id` or all `CoreId`, one `idType` per request) it
returns whether the current user follows each one, letting a page render
correct Follow/Following button state (and avoid duplicate follow requests)
for many users at once without a per-user round trip.

`MessageService.cs` implements direct messaging: `ManageMessageAsync`
(`:107-152`) creates a new `Message` (`IsRead = false`) or edits an
existing one, scoped so only the original `SenderId` can edit
(`:117`). `ToggleMessageAsync` (`:88-105`) flips read state, scoped to the
`ReceiverId`. `GetMessageConnectionsAsync` lists conversation partners with
unread counts; `GetMessagesAsync` returns a paged thread. `Message`
(`Message.cs`): `SenderId`, `ReceiverId`, `Body` (nullable), `IsRead`.

Two profile-related enums round out the social layer:
`OnlineStatus` (`src/Domain/Enumeration/OnlineStatus.cs:8-80`) computes a
presence indicator (`Online`, `ActiveRecently`, `OnlineToday`, `ActiveThisWeek`,
`ActiveThisMonth`, `ActiveLongTimeAgo`, `NewUser`) from last login date, via
threshold constants (`OnlineThreshold` 5min, `ActiveRecentlyThreshold` 1hr,
`OnlineTodayThreshold` 24hr, `ActiveThisWeekThreshold` 7d,
`ActiveThisMonthThreshold` 30d) shared with the sort described below (fixed
2026-09-01: `Calculate` previously collapsed the 24hr/7d/30d branches so
`ActiveThisWeek`/`ActiveThisMonth` were defined but never actually returned —
every login within 30 days displayed as `OnlineToday`).

**`OnlineStatus` is only ever as fresh as `ApplicationUser.LastLoginDate`, which is now touched on
every authenticated request, not just at login (fixed 2026-09-03).** Before this fix,
`LastLoginDate` had exactly one write site in the whole codebase —
`IdentityService.AddLoginHistoryAsync`, called only from the native login/token-issuing actions
(`POST identities/login`/`tokens`/`tokens/google`). Two consequences: (1) a native-auth user's
status decayed purely off time-since-login regardless of ongoing activity — with the 10-day token
lifespan (`appsettings.json`'s `TokenLifespan`), a user actively using the app for days could still
show `ActiveThisWeek`/`ActiveThisMonth`; (2) a legacy-auth-bridge user (`docs/api/authentication.md`,
"Legacy-auth bridge") never got `LastLoginDate` set **at all** — `SyncLegacyAuthAsync` and
`VerifyLegacyTokenAsync` never touched it — so every such user showed `NewUser` forever no matter
how active they actually were, likely misclassifying a large share of real users since the legacy
bridge is still the frontend's primary auth path during the gama-api migration.

Fixed by adding `IdentityService.TouchLastSeenAsync(long userId)`, called from both
`ITokenService.VerifyTokenAsync` (native) and `VerifyLegacyTokenAsync` (legacy bridge) — the one
chokepoint (`TokenAuthenticationHandler`) every authenticated request of either auth shape passes
through, regardless of which token shape it used. Throttled via the Redis-backed `ICacheProvider`
(cache key `LastSeenTouch_{userId}`, `LastSeenTouchThrottle = 4 minutes`) so the actual SQL write
(`ExecuteUpdateAsync` on just `LastLoginDate`) only happens roughly once per active user per
throttle window, not on every request — most requests are a single fast Redis `GET` that short-
circuits. Deliberately swallows its own failures (cache or DB) rather than letting them propagate,
since a hiccup here must never turn into a failed authentication for an otherwise-valid request.
Deliberately separate from `AddLoginHistoryAsync`'s own `LastLoginDate` write — that one stays tied
to real login/token-issuance events only, alongside its `LoginHistory` audit row (IP/UserAgent per
login); `TouchLastSeenAsync` is a much higher-frequency, best-effort presence signal with no audit
trail of its own.

`ProfileVisibility`
(`src/Domain/Enumeration/ProfileVisibility.cs:9-15`: `Private`, `Public`,
`ConnectionsOnly`) governs profile visibility. The check lives in
`IdentityService`, not `ConnectionService`/`MessageService`:
`GetPublicProfileAsync` (single profile, `GET identities/profiles/{handle}`) allows the request
through if the profile is `Public`, if the viewer *is* the profile owner, or — for
`ConnectionsOnly` — if a confirmed `Connection` exists between them (`Private` always 404s for
anyone but the owner); `GetProfilesListAsync` (`GET identities/profiles/list`) is simpler, hard-filtering
to `ProfileVisibility.Public` only, no `ConnectionsOnly` carve-out. Every account starts `Private`
by default (`RegisterAsync`, `SyncLegacyAuthAsync`) with one exception — see
`docs/business/identity-and-access.md`'s "New teacher accounts default to a Public profile" note.

### Default sort order for `profiles/list`

`GetProfilesListAsync` (`IdentityService.cs`, around `:1844-1907`) sorts by an
explicit client-supplied `PagingDto.SortFilter` when given (any column on the
internal projection — `LastLoginDate`, `UserRateLevel`, `Id`, `FirstName`,
`LastName`, `Handle`, `AvatarId`, `Skills`, `ActivityRank`); otherwise it falls
back to a computed `ActivityRank` tier (0 = `Online` … 4 = `ActiveThisMonth`,
5 = never logged in, 6 = `ActiveLongTimeAgo`), ascending, then `LastLoginDate`
descending, then `UserRateLevel` descending. A never-logged-in user (`LastLoginDate
= null`) intentionally ranks above someone whose one login is buried in
`ActiveLongTimeAgo` territory (fixed 2026-09-01 — previously the fallback was a
flat `OrderByDescending(LastLoginDate)`, and since this app runs on SQL Server,
where `NULL` sorts last in a `DESC` order, never-logged-in users were pushed to
the very bottom of the list instead of being distinguished from "long time
ago"). `ActivityRank` is computed as a flat sum of independent conditions
(not nested ternaries — required to satisfy this repo's Sonar analyzer, see
`CLAUDE.md`) so the whole expression still translates to one SQL expression the
database can sort and page on, rather than materializing the whole table into
memory. A composite `(ProfileVisibility, LastLoginDate DESC)` index
(`ApplicationUser.Configure`, migration `AddProfileVisibilityLastLoginDateIndex`)
backs the `ProfileVisibilityEqualsSpecification` filter this endpoint always
applies, so the underlying scan doesn't touch non-public profiles at all.

## Audit trail: LoginHistory

`LoginHistory` (`src/Domain/Entity/LoginHistory.cs`) records `UserId`,
`CreationDate`, `IpAddress` (required), `UserAgent` (optional) for each
successful sign-in — there is no failure/attempt record, only successes
appear to be logged. Written by `IdentityService.AddLoginHistoryAsync`
(`IdentityService.cs:1393-1410`), which also bumps
`ApplicationUser.LastLoginDate` in the same call; invoked from
`IdentitiesController` after each successful authentication path (login,
and at least 3 other auth flows in that controller). See
`docs/business/identity-and-access.md` for the surrounding login flow.
