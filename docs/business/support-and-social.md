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

There is **no formal status/priority workflow** — no "open/closed" or
priority/category field exists. State is tracked purely through boolean
read flags: `Ticket.IsReadByAdmin` (toggled via `ToggleIsReadByAdminAsync`,
`TicketService.cs:342-362`) and `TicketReply.IsRead`/`IsReadByAdmin`
(marked via `SetReplysAsReadedByUserAsync`/`SetReplysAsReadedByAdminAsync`,
`:298-340`). Replying sets these inversely depending on who replied
(`ReplyTicketAsync`, `:242-296`) — a user reply marks it read-by-user/
unread-by-admin and vice versa, and an admin reply triggers a confirmation
email to the ticket's `Email` (`:272-287`).

`ProccessInboundEmailAsync` (`:397-460`) supports replying to tickets by
email: it matches inbound messages to an existing ticket by a
`[Ticket-N]`-style subject pattern (regex at `:482`) and appends them as
replies, or creates a new ticket if no match is found.

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
photos/comments/issues, and blog posts/comments. `ManageReactionAsync`
(`ReactionService.cs:59-105`) looks up any existing reaction by the same
key; submitting the same `IsLike` value again is rejected as a duplicate
(`:75`), otherwise it flips or inserts.

Note: the separate `ItemType` enum (`src/Domain/Enumeration/ItemType.cs:9-15`:
`School`, `Blog`, `Profile`) is unrelated to Reactions — it's used only for
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
`OnlineStatus` (`src/Domain/Enumeration/OnlineStatus.cs:11-29`) computes a
presence indicator (`Online`, `ActiveRecently`, ... `NewUser`) from last
login date; `ProfileVisibility`
(`src/Domain/Enumeration/ProfileVisibility.cs:9-15`: `Private`, `Public`,
`ConnectionsOnly`) governs profile visibility, though the actual visibility
check was not found inside `ConnectionService`/`MessageService` and likely
lives in a profile-viewing path not reviewed here.

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
