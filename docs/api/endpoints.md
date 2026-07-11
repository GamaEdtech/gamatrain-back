# Endpoint Catalog

Full inventory of every controller and action under `src/Presentation/Api/Controllers`
(public), `src/Presentation/Api/Areas/Admin/Controllers` (Admin-role gated), and
`src/Presentation/Api/Areas/Finance/Controllers` (Finance-role gated). See `overview.md` for the
envelope shape and `authentication.md` for what "Anonymous / User / Admin / Finance / ApiKey" mean
mechanically.

Conventions used below:
- **Route** is the literal string passed to `[HttpGet("...")]`/`[HttpPost("...")]`/etc., appended
  to the controller's route prefix. An empty route means the action responds at the bare
  controller path.
- **Request model** names the view model bound from the body/form/query; route-only primitive
  params are listed by name instead. Full field definitions live under
  `src/Presentation/ViewModel/<Feature>/`.
- **Response model** is the `T` inside `ApiResponse<T>` (or `ApiResponseWithFilter<T>`) that the
  action actually returns — i.e. what you'll find under `data` in the JSON body. View model classes
  live under the same `src/Presentation/ViewModel/<Feature>/` folders.
- A few actions have a mismatch between their `[Produces(...)]` attribute and what the method body
  actually returns (copy/paste artifacts); where found, both the annotated type and the real
  behavior are noted.
- All controllers below declare `[ApiVersion("1.0")]` unless noted otherwise (a couple of files
  omit it, flagged inline) — there is only one API version today.

---

## Public controllers (`src/Presentation/Api/Controllers/`)

Base route: `api/v{version:apiVersion}/[controller]` (controller name lowercased), e.g.
`SchoolsController` → `api/v1/schools`.

### BlogsController
`src/Presentation/Api/Controllers/BlogsController.cs` — class-level `[Permission(policy: null)]` (User by default; several actions override with `[AllowAnonymous]`)

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| GET | `posts` | List published blog posts, filterable by tag/visibility/date/title | Anonymous | `PostsRequestViewModel` (query) | `ListDataSource<PostsResponseViewModel>` |
| GET | `posts/random` | Get a random set of published posts | Anonymous | `RandomPostsRequestViewModel` (query) | `ListDataSource<PostsResponseViewModel>` |
| GET | `posts/{postId:long}` | Get a single published post; increments view count in background | Anonymous | route: `postId` | `PostResponseViewModel` |
| DELETE | `posts/{postId:long}` | Remove a post (only if caller is its creator) | User | route: `postId` | `bool` |
| PATCH | `posts/{postId:long}/like` | Like a post | User | route: `postId` | `bool` |
| PATCH | `posts/{postId:long}/dislike` | Dislike a post | User | route: `postId` | `bool` |
| GET | `slugs/generate` | Generate a unique slug from a title | User | query: `title` | `string` |
| GET | `slugs/validate` | Check whether a slug is available | User | query: `slug` | `bool` |
| GET | `contributions` | List current user's post contributions | User | `PostContributionListRequestViewModel` (query) | `ListDataSource<PostContributionListResponseViewModel>` |
| GET | `contributions/{contributionId:long}` | Get a single post contribution owned by caller | User | route: `contributionId` | `PostContributionResponseViewModel` |
| POST | `contributions` | Create a new post contribution | User | `PostContributionViewModel` (form) | `ManagePostContributionResponseViewModel` |
| PUT | `contributions/{contributionId:long}` | Update an existing post contribution (must be creator) | User | `UpdatePostContributionViewModel` (form) + route `contributionId` | `ManagePostContributionResponseViewModel` |
| GET | `posts/{postId:long}/comments` | List comments on a post | Anonymous | `PostCommentsRequestViewModel` (query) + route `postId` | `ListDataSource<PostCommentsResponseViewModel>` |
| POST | `posts/{postId:long}/comments` | Create a comment on a post (captcha-verified) | User | `ManagePostCommentRequestViewModel` (body) + route `postId` | `ManagePostCommentResponseViewModel` |
| PATCH | `posts/{postId:long}/comments/{commentId:long}/like` | Like a comment | User | route params | `bool` |
| PATCH | `posts/{postId:long}/comments/{commentId:long}/dislike` | Dislike a comment | User | route params | `bool` |

### BoardsController
`src/Presentation/Api/Controllers/BoardsController.cs` — class-level `[Permission(policy: null)]` + `[AllowAnonymous]` (whole controller anonymous)

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| GET | `` | List all education boards (`[ResponseCache(Duration=300)]`) | Anonymous | none | `IEnumerable<BoardsListResponseViewModel>` |

### ConnectionsController
`src/Presentation/Api/Controllers/ConnectionsController.cs` — class-level `[Permission(policy: null)]` (User for all actions, no anonymous overrides)

All `users/{id:long}/...` actions below (`followers`, `followings`, `follow`, `unfollow`,
`subscriptions/toggle`) additionally accept an optional `idType` **query string** parameter
(values: `Id` (default) or `CoreId`, case-insensitive; declared as `string?` on the action, not the
`IdentifierType` smart enum directly — Swashbuckle expands a query-bound smart-enum parameter into
its internal properties (`Name`, `Value`, ...) instead of a single named parameter, so a plain
string is parsed internally instead) — when `CoreId`, `id` is resolved against
`ApplicationUser.CoreId` (the legacy gama-api link) instead of the local `Id`, via
`IIdentityService.ResolveUserIdAsync`. Returns a `UserNotFound` error (no auto-creation) if the
`CoreId` isn't linked to any local user yet. See `docs/business/support-and-social.md`.

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| GET | `requests` | Get current user's incoming follow requests | User | `FollowRequestsRequestViewModel` (query) | `ListDataSource<FollowRequestsResponseViewModel>` |
| PATCH | `{id:long}/confirm` | Confirm a follow request | User | `ConfirmFollowRequestRequestViewModel` (body) + route `id` | `bool` |
| PATCH | `{id:long}/reject` | Reject a follow request | User | route: `id` | `bool` |
| GET | `users/{id:long}/followers` | List followers of a user | User | `FollowersRequestViewModel` (query) + route `id` + query `idType` | `ListDataSource<FollowViewModel>` |
| GET | `users/{id:long}/followings` | List users a given user follows | User | `FollowingsRequestViewModel` (query) + route `id` + query `idType` | `ListDataSource<FollowViewModel>` |
| POST | `users/{id:long}/follow` | Follow a user | User | `FollowRequestViewModel` (body) + route `id` + query `idType` | `bool` |
| POST | `users/{id:long}/unfollow` | Unfollow a user | User | `UnFollowRequestViewModel` (body) + route `id` + query `idType` | `bool` |
| PATCH | `users/{id:long}/subscriptions/toggle` | Toggle subscription to a user's activity feed | User | route: `id` + query `idType` | `bool` |
| POST | `status` | Bulk-check whether the current user follows each of a list of users (by `Id` or `CoreId`, one `idType` per request) — for "Follow"/"Following" button state, avoids duplicate follow requests | User | `ConnectionStatusRequestViewModel` (body) | `IEnumerable<ConnectionStatusResponseViewModel>` |

### ExamsController
`src/Presentation/Api/Controllers/ExamsController.cs` — class-level `[Permission(policy: null)]` (User). **Deviation:** no `[ApiVersion]` attribute (only `[ApiController]`).

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| GET | `export` | Export an exam to a file, gated by a `SecretKey` header | User | `ExportExamRequestViewModel` (query) + `SecretKey` header | Declared `IActionResult`; error path returns `ApiResponse<Void>`, success path returns a raw `FileContentResult` (binary file), not the envelope |

### ExperiencesController
`src/Presentation/Api/Controllers/ExperiencesController.cs` — class-level `[Permission(policy: null)]` (User, no anonymous overrides)

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| GET | `` | List current user's experiences | User | `ExperiencesRequestViewModel` (query) | `ListDataSource<ExperienceResponseViewModel>` |
| GET | `{id:long}` | Get a single experience owned by caller | User | route: `id` | `ExperienceResponseViewModel` |
| POST | `` | Create a new experience entry | User | `ManageExperienceRequestViewModel` (body) | `ManageExperienceResponseViewModel` |
| PUT | `{id:long}` | Update an existing experience entry | User | `ManageExperienceRequestViewModel` (body) + route `id` | `ManageExperienceResponseViewModel` |
| DELETE | `{id:long}` | Remove an experience entry (owned by caller) | User | route: `id` | `bool` |

### FilesController
`src/Presentation/Api/Controllers/FilesController.cs` — class-level `[Permission(policy: null)]` + `[AllowAnonymous]` (whole controller anonymous)

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| GET | `{containerType:ContainerType}/{id}` | Resolve a stored file's URL and redirect to it | Anonymous | route: `containerType` (enum), `id` | Declared `ApiResponse<string>`; actually returns empty `ApiResponse<string>` (not found) or an HTTP redirect (`RedirectResult`) |

### GamesController
`src/Presentation/Api/Controllers/GamesController.cs` — **no class-level auth attribute** (each action carries its own). Now carries `[ApiVersion("1.0")]` + `[ApiVersion("2.0")]` (added for the `spends` split below) — every other action is unmapped and so still serves both versions unchanged.

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| GET | `easter-egg/fortune-wheel` | Spin the "fortune wheel" easter egg, awarding coins | **ApiKey** | none | `CoinsResponseViewModel` |
| POST | `easter-egg/points` | Award points for an easter-egg interaction | User | `EasterEggPointsRequestViewModel` (body) | `EasterEggPointsResponseViewModel` |
| POST | `test-time` | Record a "test time" quiz answer/timing | User | `TestTimeQuizRequestViewModel` (body) | `TestTimeQuizResponseViewModel` |
| POST | `exams/points` | Award points for exam completion, gated by `SecretKey` header | User | `ExamPointsRequestViewModel` (body) + `SecretKey` header | `ExamPointsResponseViewModel` (note: `[Produces]` attribute misnames this as `TestTimeQuizResponseViewModel`) |
| POST | `spends` (v1.0) | Spend on pastpaper/test download — tries the caller's subscription quota first (see `docs/business/subscriptions.md`), falls back to wallet points unchanged | User | `SpendPointsRequestViewModel` (body) | `bool` (unchanged wire shape) |
| POST | `spends` (v2.0) | Same action as v1, richer response for clients that want to show which path paid and any upgrade upsell | User | `SpendPointsRequestViewModel` (body) | `SpendPointsResponseViewModel` (`spent`, `paidBy`, `remainingQuota`, `upgradeSuggestions[]`) |

### GradesController
`src/Presentation/Api/Controllers/GradesController.cs` — class-level `[Permission(policy: null)]` + `[AllowAnonymous]` (whole controller anonymous)

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| GET | `` | List grades, optionally filtered by board id | Anonymous | `GradesRequestViewModel` (query) | `ListDataSource<GradesResponseViewModel>` |

### HomeController
`src/Presentation/Api/Controllers/HomeController.cs` — **no `[Route]`, no `[ApiVersion]`, no auth attribute at all**; not built on `ApiControllerBase<T>`'s versioned route, doesn't use the `ApiResponse<T>` envelope. Effectively a non-API MVC controller.

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| GET | (default MVC route) | Redirects `/` to `/swagger` | Anonymous (implicit — no `[Authorize]`-derived attribute present) | none | none (plain redirect) |

### IdentitiesController
`src/Presentation/Api/Controllers/IdentitiesController.cs` — **no class-level auth attribute**; every action annotates itself. See `authentication.md` for the login/token flows in detail.

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| POST | `login` | Authenticate with username/password; issues the Identity cookie | Anonymous | `AuthenticationRequestViewModel` (body) | `AuthenticationResponseViewModel` |
| POST | `register` | Register a new local account; queues welcome email | Anonymous | `RegistrationRequestViewModel` (body) | `Void` (no data) |
| GET | `logout` | Sign the current user out | User | none | `Void` (no data) |
| PUT | `password` | Change current user's password | User | `ChangePasswordRequestViewModel` (body) | `Void` (no data) |
| POST | `tokens` | Authenticate and issue an opaque bearer token | Anonymous | `GenerateTokenRequestViewModel` (body) | `GenerateTokenResponseViewModel` |
| POST | `tokens/old` | (doc comment: "temporary, must delete") Exchange a legacy/core token for a new token | Anonymous | `GenerateTokenWithOldRequestViewModel` (body) | `GenerateTokenResponseViewModel` |
| POST | `tokens/google` | Authenticate via Google OAuth and issue a bearer token | Anonymous | `GenerateTokenWithGoogleRequestViewModel` (body) | `GenerateTokenResponseViewModel` |
| POST | `tokens/revoke` | Revoke current user's API access token | User | none | `RevokeTokenResponseViewModel` |
| GET | `authenticated` | Check whether the current request is authenticated | Anonymous (class default overridden) | none | `bool` |
| GET | `profiles` | Get current user's own profile settings | User | none | `ProfileSettingsResponseViewModel` |
| GET | `profiles/list` | Search/list public profiles by name/skill | Anonymous | `PublicProfileListRequestViewModel` (query) | `ListDataSource<PublicProfileListResponseViewModel>` |
| GET | `profiles/{handle}` | Get a public profile by handle | Anonymous | route: `handle` | `PublicProfileResponseViewModel` |
| PUT | `profiles` | Update current user's profile settings | User | `ProfileSettingsRequestViewModel` (body) | `bool` |
| PATCH | `profiles/avatars` | Set/update current user's avatar | User | `ManageAvatarRequestViewModel` (body) | `bool` |
| DELETE | `profiles/avatars` | Remove current user's avatar | User | none | `bool` |
| GET | `leader-board` | Get top 100 users by points, filterable by board/grade/location/date | Anonymous | `Top100UsersRequestViewModel` (query, nullable) | `IEnumerable<UserPointsViewModel>` |
| DELETE | `profiles` | Request deletion of own account (re-authenticates first) | User | `DeleteAccountRequestViewModel` (body) | `bool` |
| PATCH | `profiles/recover` | Cancel a pending account-deletion request (re-authenticates first) | User | `RecoverAccountRequestViewModel` (body) | `bool` |
| GET | `handles/validate` | Check whether a profile handle is available | User | query: `handle` | `string` |

### LegacyAuthBridgeController — temporary
`src/Presentation/Api/Controllers/LegacyAuthBridgeController.cs` — route `api/v1/legacy-auth`, class-level `[AllowAnonymous]`. Proxies gama-api (the old backend); see `authentication.md`'s "Legacy-auth bridge" section. Slated for removal once the frontend migrates off gama-api.

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| POST | `login` | Proxy gama-api login; sync local user, return gama-api's own token unchanged. Weak passwords get an OTP step-up instead (`Type`/`Code` fields, `type: "confirm"` to complete — see `authentication.md`) | Anonymous | `LegacyLoginRequestViewModel` (body) | `LegacyAuthTokenResponseViewModel` |
| POST | `google` | Proxy gama-api googleAuth; same sync behavior as `login` | Anonymous | `LegacyGoogleAuthRequestViewModel` (body) | `LegacyAuthTokenResponseViewModel` |
| POST | `register` | Pure passthrough to gama-api register (multi-step OTP); no local sync, no token | Anonymous | `LegacyOtpFlowRequestViewModel` (body) | `LegacyMessageResponseViewModel` |
| POST | `recovery` | Pure passthrough to gama-api recovery/reset-password (multi-step OTP); no local sync, no token | Anonymous | `LegacyOtpFlowRequestViewModel` (body) | `LegacyMessageResponseViewModel` |

### LanguagesController
`src/Presentation/Api/Controllers/LanguagesController.cs` — class-level `[Permission(policy: null)]` + `[AllowAnonymous]` (whole controller anonymous)

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| GET | `` | List active languages (with RTL flag) | Anonymous | none | `IEnumerable<ActiveLanguageViewModel>` |
| GET | `time-zones` | List available time zones | Anonymous | none | `IEnumerable<TimeZoneViewModel>` |

### LocationsController
`src/Presentation/Api/Controllers/LocationsController.cs` — class-level `[Permission(policy: null)]` + `[AllowAnonymous]` (whole controller anonymous)

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| GET | `countries` | List countries (paged) | Anonymous | `LocationsRequestViewModel` (query) | `ListDataSource<LocationsResponseViewModel>` |
| GET | `states/{countryId}` | List states of a country (paged) | Anonymous | route: `countryId` + `LocationsRequestViewModel` (query) | `ListDataSource<LocationsResponseViewModel>` |
| GET | `cities/{stateId}` | List cities of a state (paged) | Anonymous | route: `stateId` + `LocationsRequestViewModel` (query) | `ListDataSource<LocationsResponseViewModel>` |

### MessagesController
`src/Presentation/Api/Controllers/MessagesController.cs` — class-level `[Permission(policy: null)]` (User, no anonymous overrides)

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| GET | `connections` | List current user's message connections with unread counts | User | none | `IEnumerable<MessageConnectionResponseViewModel>` |
| GET | `connections/{connectionId:long}` | List messages within a connection (paged) | User | route: `connectionId` + `MessagesRequestViewModel` (query) | `ListDataSource<MessagesResponseViewModel>` |
| POST | `` | Send a new message | User | `ManageMessageRequestViewModel` (body) | `Void` (no data) |
| PUT | `{id:long}` | Edit an existing message | User | route: `id` + `ManageMessageRequestViewModel` (body) | `Void` (no data) |
| PATCH | `{id:long}/toggle` | Toggle a message's read/unread state | User | route: `id` | `Void` (no data) |
| DELETE | `{id:long}` | Remove an unread message sent by the current user | User | route: `id` | `bool` |

### PaymentsController
`src/Presentation/Api/Controllers/PaymentsController.cs` — class-level `[Permission(policy: null)]` (User, no anonymous overrides). `VerifyPayment` has known hardening needs around concurrent verification and caller authorization — see [`docs/business/payments-and-points.md`](../business/payments-and-points.md) (details kept in an internal, non-public review rather than this repo). `VerifyPayment` also now branches on whether the payment was created for a subscription purchase (see `SubscriptionsController.PurchaseSubscription` below and `docs/business/subscriptions.md`) — same route and response shape either way, only the server-side effect differs.

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| POST | `` | Create a payment (returns gateway redirect URL) | User | `CreatePaymentRequestViewModel` (body) | `CreatePaymentResponseViewModel` |
| POST | `{id:long}/verify` | Verify a payment transaction with the gateway; activates a subscription instead of crediting points when the payment was for one | User | route: `id` + `VerifyPaymentRequestViewModel` (body) | `bool` |

### QuestionsController
`src/Presentation/Api/Controllers/QuestionsController.cs` — class-level `[Permission(policy: null)]` (User, no anonymous overrides)

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| GET | `` | Get random questions (correct-answer indices AES-encrypted using caller's token as key material) | User | `RandomQuestionsRequestViewModel` (query) | `IEnumerable<RandomQuestionResponseViewModel>` |

### ReferralController
`src/Presentation/Api/Controllers/ReferralController.cs` — class-level `[Permission(policy: null)]` (User, no anonymous overrides)

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| POST | `generate` | Generate (or fetch) the current user's referral ID | User | none | `ReferralReponseViewModel` |

### SchoolsController
`src/Presentation/Api/Controllers/SchoolsController.cs` — class-level `[Permission(policy: null)]`; several read actions override with `[AllowAnonymous]`

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| GET | `` | Search/list schools (country/state/city/name/score/image/board/tuition/geo radius, paged) | Anonymous | `SchoolInfoRequestViewModel` (query) | `ListDataSource<SchoolInfoResponseViewModel>` (via `OkWithFilter`, echoes filters) |
| GET | `{id:long}` | Get full school detail; enqueues background view-count increment | Anonymous | route: `id` | `SchoolResponseViewModel` |
| GET | `{schoolId:long}/rate` | Get aggregated rating breakdown for a school | Anonymous | route: `schoolId` | `SchoolRateResponseViewModel` |
| GET | `{schoolId:long}/comments` | List comments for a school (paged) | Anonymous | route: `schoolId` + `SchoolCommentsRequestViewModel` (query) | `ListDataSource<SchoolCommentsResponseViewModel>` |
| POST | `{schoolId:long}/comments` | Submit a school comment/rating (captcha) as a contribution | User | route: `schoolId` + `ManageSchoolCommentRequestViewModel` (body) | `ManageSchoolCommentResponseViewModel` |
| PATCH | `{schoolId:long}/comments/{commentId:long}/like` | Like a school comment | User | route params | `bool` |
| PATCH | `{schoolId:long}/comments/{commentId:long}/dislike` | Dislike a school comment | User | route params | `bool` |
| GET | `{schoolId:long}/images/{fileType:ImageFileType}` | List school images of a given file type | Anonymous | route: `schoolId`, `fileType` (enum) | `IEnumerable<SchoolImageInfoViewModel>` |
| POST | `{schoolId:long}/images` | Upload/contribute a new school image | User | route: `schoolId` + `CreateSchoolImageRequestViewModel` (multipart form) | `CreateSchoolImageResponseViewModel` |
| DELETE | `{schoolId:long}/images/{contributionId:long}` | Remove the caller's own school-image contribution | User | route params | `bool` |
| POST | `{schoolId:long}/images/{imageId:long}/contributions` | Request removal of a school image | User | route params + `RemoveSchoolImageContributionRequestViewModel` (body) | `RemoveSchoolImageContributionResponseViewModel` (note: `[Produces]` attribute misnames this as `bool`) |
| GET | `{schoolId:long}/contributions` | List caller's contributions for a school (paged) | User | route: `schoolId` + `SchoolContributionListRequestViewModel` (query) | `ListDataSource<SchoolContributionInfoListResponseViewModel>` |
| GET | `{schoolId:long}/contributions/{contributionId:long}` | Get a single caller-owned school contribution's detail | User | route params | `SchoolContributionViewModel` |
| POST | `{schoolId:long}/contributions` | Propose edits to an existing school | User | route: `schoolId` + `ManageSchoolContributionRequestViewModel` (body) | `ManageSchoolContributionResponseViewModel` |
| PUT | `{schoolId:long}/contributions/{contributionId:long}` | Update an existing school-edit contribution | User | route params + `ManageSchoolContributionRequestViewModel` (body) | `ManageSchoolContributionResponseViewModel` |
| POST | `contributions` | Propose a brand-new school (with optional default image/comment) | User | `ManageNewSchoolContributionRequestViewModel` (multipart form) | `ManageSchoolContributionResponseViewModel` |
| GET | `{schoolId:long}/issues` | List caller's "school issue" contributions for a school (paged) | User | route: `schoolId` + `SchoolIssuesContributionListRequestViewModel` (query) | `ListDataSource<SchoolIssuesContributionResponseViewModel>` |
| POST | `{schoolId:long}/issues` | Report an issue about a school | User | route: `schoolId` + `ManageSchoolIssuesContributionRequestViewModel` (body) | `ManageSchoolIssuesContributionResponseViewModel` |

### SubjectsController
`src/Presentation/Api/Controllers/SubjectsController.cs` — class-level `[Permission(policy: null)]` + `[AllowAnonymous]` (whole controller anonymous)

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| GET | `` | List subjects, optionally filtered by grade (paged) | Anonymous | `SubjectsRequestViewModel` (query) | `ListDataSource<SubjectsResponseViewModel>` |

### SubscriptionsController
`src/Presentation/Api/Controllers/SubscriptionsController.cs` — class-level `[Permission(policy: null)]` (User, no anonymous overrides). See `docs/business/subscriptions.md` for the purchase → verify → activate lifecycle and quota model.

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| GET | `plans` | List active subscription plans available at the current user's (geo) location, with resolved price and feature/quota list per plan | User | none | `IEnumerable<ActiveSubscriptionPlanResponseViewModel>` |
| POST | `plans/{id:long}/purchase` | Start a subscription purchase: resolves price server-side, creates a `Pending` `UserSubscription` + `Payment`, returns the gateway checkout URL | User | route: `id` + `PurchaseSubscriptionRequestViewModel` (body: `Gateway`) | `PurchaseSubscriptionResponseViewModel` |
| GET | `me` | Get the current user's active subscription, including per-feature quota (`limit`/`used`/`remaining`) | User | none | `UserSubscriptionResponseViewModel` |

### TagsController
`src/Presentation/Api/Controllers/TagsController.cs` — class-level `[Permission(policy: null)]` + `[AllowAnonymous]` (whole controller anonymous)

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| GET | `{tagType:TagType}` | List up to 100 tags of a given tag type | Anonymous | route: `tagType` (enum) | `IEnumerable<TagsResponseViewModel>` |

### TicketsController
`src/Presentation/Api/Controllers/TicketsController.cs` — class-level `[Permission(policy: null)]`; `CreateTicket` and `InboundWebHook` override with `[AllowAnonymous]`

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| GET | `` | List current user's tickets (paged) | User | `TicketsRequestViewModel` (query) | `ListDataSource<TicketsResponseViewModel>` |
| GET | `{id:long}` | Get a caller-owned ticket's details; marks read-by-admin | User | route: `id` | `TicketResponseViewModel` |
| GET | `{id:long}/replys` | List replies on a ticket; marks read-by-user | User | route: `id` | `IEnumerable<TicketReplyResponseViewModel>` |
| POST | `{id:long}/replys` | Reply to a ticket as the user, optional file attachment | User | route: `id` + `ReplyTicketByUserRequestViewModel` (multipart form) | `Void` (no data) |
| POST | `` | Create a new support ticket (captcha); works whether or not caller is authenticated | Anonymous (usable while authenticated too) | `CreateTicketRequestViewModel` (multipart form) | `ManageTicketResponseViewModel` |
| POST | `inbound-webhook` | Inbound email webhook — parses raw HTTP request into ticket replies | Anonymous | none (reads raw `Request` directly) | `Void` (no data) |

### TopicsController
`src/Presentation/Api/Controllers/TopicsController.cs` — class-level `[Permission(policy: null)]` + `[AllowAnonymous]` (whole controller anonymous)

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| GET | `` | List topics, optionally filtered by subject (paged) | Anonymous | `TopicsRequestViewModel` (query) | `ListDataSource<TopicsResponseViewModel>` |

### TransactionsController
`src/Presentation/Api/Controllers/TransactionsController.cs` — class-level `[Permission(policy: null)]` (User, no anonymous overrides)

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| GET | `` | List current user's transactions, filterable by debit/credit and type (paged) | User | `TransactionsRequestViewModel` (query) | `ListDataSource<TransactionsResponseViewModel>` |
| GET | `balance` | Get current user's point balance | User | none | `long` |
| GET | `statistics` | Get transaction debit/credit statistics grouped by period | User | `TransactionStatisticsRequestViewModel` (query) | `IEnumerable<TransactionStatisticsResponseViewModel>` |

### VotingPowersController
`src/Presentation/Api/Controllers/VotingPowersController.cs` — class-level `[Permission(policy: null)]` + `[AllowAnonymous]` (whole controller anonymous, **including the write action below**)

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| GET | `` | List voting powers, filterable by wallet address / proposal id (paged) | Anonymous | `VotingPowersRequestViewModel` (query) | `ListDataSource<VotingPowersResponseViewModel>` |
| POST | `` | Bulk-import voting powers; access-controlled via an in-body signature check rather than a standard auth attribute | Anonymous (at the HTTP-auth-attribute layer) | `CreateVotingPowerRequestViewModel` (body: `PublicKey`, `Message`, `SignedMessage`, `Data[]`) | `Void` (no data) |

---

## Admin controllers (`src/Presentation/Api/Areas/Admin/Controllers/`)

Base route: `api/v{version:apiVersion}/[area]/[controller]` → `api/v1/admin/<controller>`.
Every controller in this area declares class-level `[Common.DataAnnotation.Area(nameof(Admin), "Admin")]`
(or the equivalent `nameof(Role.Admin)` form — same resolved area name) and class-level
`[Permission(Roles = [nameof(Role.Admin)])]`. **No action in any of the 18 Admin controllers
carries `[AllowAnonymous]` or a different role** — the whole area is uniformly Admin-only; the
Auth column is omitted per-row below and stated once per controller instead.

### ApplicationSettingsController — Admin-only
`src/Presentation/Api/Areas/Admin/Controllers/ApplicationSettingsController.cs` — route `api/v1/admin/applicationsettings`

| Verb | Route | Purpose | Request model | Response model |
|---|---|---|---|---|
| GET | `` | Get current application settings (points, templates, timezone, page size) | none | `ApplicationSettingsViewModel` |
| PUT | `` | Update application settings | `ApplicationSettingsViewModel` (body) | `bool` |

### BlogsController — Admin-only
`src/Presentation/Api/Areas/Admin/Controllers/BlogsController.cs` — route `api/v1/admin/blogs`

| Verb | Route | Purpose | Request model | Response model |
|---|---|---|---|---|
| GET | `contributions` | List post contributions (filter by status/date/email/username) | `PostContributionListRequestViewModel` (query) | `ListDataSource<PostContributionListResponseViewModel>` |
| GET | `contributions/{contributionId:long}` | Get a single post contribution's detail | route: `contributionId` | `PostContributionResponseViewModel` |
| PATCH | `contributions/{contributionId:long}/confirm` | Confirm/approve a post contribution | route: `contributionId` | `bool` |
| PATCH | `contributions/{contributionId:long}/reject` | Reject a post contribution with comment | `RejectPostContributionRequestViewModel` (body) + route `contributionId` | `bool` |
| PUT | `posts/{postId:long}` | Create/update a blog post | `UpdatePostRequestViewModel` (multipart form) + route `postId` | `ManagePostResponseViewModel` |
| DELETE | `posts/{postId:long}` | Delete a blog post | route: `postId` | `bool` |
| GET | `posts/comments/contributions` | List post-comment contributions | `PostCommentContributionListRequestViewModel` (query) | `ListDataSource<PostCommentContributionListResponseViewModel>` |
| GET | `posts/comments/contributions/{contributionId:long}` | Get a single post-comment contribution's detail | route: `contributionId` | `PostCommentContributionReviewViewModel` |
| PATCH | `posts/comments/contributions/{contributionId:long}/confirm` | Confirm a post-comment contribution | route: `contributionId` | `bool` |
| PATCH | `posts/comments/contributions/{contributionId:long}/reject` | Reject a post-comment contribution with comment | `RejectPostContributionRequestViewModel` (body) + route `contributionId` | `bool` |
| GET | `site-maps` | List sitemap entries for blog posts | `SiteMapListRequestViewModel` (query) | `ListDataSource<SiteMapListResponseViewModel>` |
| POST | `{postId:long}/site-maps` | Create a sitemap entry for a post | `ManageSiteMapRequestViewModel` (body) + route `postId` | `ManageSiteMapResponseViewModel` |
| PUT | `{postId:long}/site-maps/{id:long}` | Update a sitemap entry for a post | `ManageSiteMapRequestViewModel` (body) + route params | `ManageSiteMapResponseViewModel` |
| DELETE | `{postId:long}/site-maps/{id:long}` | Remove a sitemap entry for a post | route params | `bool` |

### BoardsController — Admin-only
`src/Presentation/Api/Areas/Admin/Controllers/BoardsController.cs` — route `api/v1/admin/boards`

| Verb | Route | Purpose | Request model | Response model |
|---|---|---|---|---|
| GET | `` | List boards | `BoardsRequestViewModel` (query) | `ListDataSource<BoardsResponseViewModel>` |
| GET | `{id:int}` | Get board detail | route: `id` | `BoardResponseViewModel` |
| POST | `` | Create a board | `ManageBoardRequestViewModel` (body) | `ManageBoardResponseViewModel` |
| PUT | `{id:int}` | Update a board | `ManageBoardRequestViewModel` (body) + route `id` | `ManageBoardResponseViewModel` |
| DELETE | `{id:int}` | Delete a board | route: `id` | `bool` |

### ContentLocalizationsController — Admin-only
`src/Presentation/Api/Areas/Admin/Controllers/ContentLocalizationsController.cs` — route `api/v1/admin/contentlocalizations`

| Verb | Route | Purpose | Request model | Response model |
|---|---|---|---|---|
| GET | `` | List content localization entries | `ContentLocalizationsRequestViewModel` (query) | `ListDataSource<ContentLocalizationsResponseViewModel>` |
| GET | `{id:long}` | Get content localization detail | route: `id` | `ContentLocalizationResponseViewModel` |
| POST | `` | Create a content localization entry | `ManageContentLocalizationRequestViewModel` (body) | `ManageContentLocalizationResponseViewModel` |
| PUT | `{id:long}` | Update a content localization entry | `ManageContentLocalizationRequestViewModel` (body) + route `id` | `ManageContentLocalizationResponseViewModel` |
| DELETE | `{id:long}` | Delete a content localization entry | route: `id` | `bool` |

### EmailsController — Admin-only
`src/Presentation/Api/Areas/Admin/Controllers/EmailsController.cs` — route `api/v1/admin/emails`. (Derives from `LocalizableApiControllerBase<T>` rather than `ApiControllerBase<T>`.)

| Verb | Route | Purpose | Request model | Response model |
|---|---|---|---|---|
| POST | `` | Send an email to explicit addresses and/or specified user IDs | `SendEmailRequestViewModel` (body) | `Void` (no data) |
| GET | `addresses` | Get list of configured sender email addresses | none | `IEnumerable<string>` |

### GeneralController — Admin-only
`src/Presentation/Api/Areas/Admin/Controllers/GeneralController.cs` — route `api/v1/admin/general`

| Verb | Route | Purpose | Request model | Response model |
|---|---|---|---|---|
| GET | `system-claims` | Get list of system claim names (flags enum member names) | none | `IEnumerable<string>?` |

### GradesController — Admin-only
`src/Presentation/Api/Areas/Admin/Controllers/GradesController.cs` — route `api/v1/admin/grades`

| Verb | Route | Purpose | Request model | Response model |
|---|---|---|---|---|
| GET | `` | List grades, optionally filtered by board | `GradesRequestViewModel` (query) | `ListDataSource<GradesResponseViewModel>` |
| GET | `{id:int}` | Get grade detail | route: `id` | `GradeResponseViewModel` |
| POST | `` | Create a grade | `ManageGradeRequestViewModel` (body) | `ManageGradeResponseViewModel` |
| PUT | `{id:int}` | Update a grade | `UpdateGradeRequestViewModel` (body) + route `id` | `ManageGradeResponseViewModel` |
| DELETE | `{id:int}` | Delete a grade | route: `id` | `bool` |

### IdentitiesController — Admin-only
`src/Presentation/Api/Areas/Admin/Controllers/IdentitiesController.cs` — route `api/v1/admin/identities`. (Derives from `LocalizableApiControllerBase<T>`.)

| Verb | Route | Purpose | Request model | Response model |
|---|---|---|---|---|
| GET | `` | List users (filter by referral, name, email, referral ID, roles) | `UserListRequestViewModel` (query) | `ListDataSource<UserListResponseViewModel>` |
| GET | `{userId:long}` | Get user detail | route: `userId` | `UserResponseViewModel` |
| POST | `` | Create a user | `CreateUserRequestViewModel` (body) | `Void` (no data) |
| PUT | `{userId:long}` | Edit a user | `EditUserRequestViewModel` (body) + route `userId` | `Void` (no data) |
| DELETE | `{userId:long}` | Delete a user | route: `userId` | `bool` |
| PATCH | `{userId:long}/toggle` | Enable/disable a user | route: `userId` | `Void` (no data) |
| GET | `{userId:long}/token` | View a user's API access token | route: `userId` | `GetTokenResponseViewModel` |
| POST | `{userId:long}/token` | Generate a new API access token for a user | route: `userId` | `GenerateTokenResponseViewModel` |
| DELETE | `{userId:long}/token` | Revoke a user's API access token | route: `userId` | `bool` |
| PUT | `{userId:long}/reset-password` | Reset a user's password | `ResetPasswordRequestViewModel` (body) + route `userId` | `Void` (no data) |
| GET | `{userId:long}/permissions` | View a user's permission tree, roles, and system claims | route: `userId` | `UserPermissionsResponseViewModel` |
| PUT | `{userId:long}/permissions` | Update a user's permissions, roles, and system claims | `ManageUserPermissionsRequestViewModel` (body) + route `userId` | `Void` (no data) |

### LanguagesController — Admin-only
`src/Presentation/Api/Areas/Admin/Controllers/LanguagesController.cs` — route `api/v1/admin/languages`

| Verb | Route | Purpose | Request model | Response model |
|---|---|---|---|---|
| GET | `` | List languages | `LanguagesRequestViewModel` (query) | `ListDataSource<LanguageResponseViewModel>` |
| GET | `{id:int}` | Get language detail | route: `id` | `LanguageResponseViewModel` |
| POST | `` | Create a language | `ManageLanguageRequestViewModel` (body) | `ManageLanguageResponseViewModel` |
| PUT | `{id:int}` | Update a language | `ManageLanguageRequestViewModel` (body) + route `id` | `ManageLanguageResponseViewModel` |
| DELETE | `{id:int}` | Delete a language | route: `id` | `bool` |
| GET | `cultures` | List all available .NET cultures (code + native name) | none | `IEnumerable<CultureViewModel>` |

### LocationsController — Admin-only
`src/Presentation/Api/Areas/Admin/Controllers/LocationsController.cs` — route `api/v1/admin/locations`

| Verb | Route | Purpose | Request model | Response model |
|---|---|---|---|---|
| GET | `countries` | List countries | `LocationsRequestViewModel` (query) | `ListDataSource<LocationsResponseViewModel>` |
| GET | `countries/{id:int}` | Get a country by id | route: `id` | `LocationResponseViewModel` |
| POST | `countries` | Create a country | `ManageLocationRequestViewModel` (body) | `ManageLocationResponseViewModel` |
| PUT | `countries/{id:int}` | Update a country | `UpdateLocationRequestViewModel` (body) + route `id` | `ManageLocationResponseViewModel` |
| DELETE | `countries/{id:int}` | Remove a country | route: `id` | `bool` |
| GET | `states` | List states | `LocationsRequestViewModel` (query) | `ListDataSource<LocationsResponseViewModel>` |
| GET | `states/{id:int}` | Get a state by id | route: `id` | `LocationResponseViewModel` |
| POST | `states` | Create a state | `ManageLocationRequestViewModel` (body) | `ManageLocationResponseViewModel` |
| PUT | `states/{id:int}` | Update a state | `UpdateLocationRequestViewModel` (body) + route `id` | `ManageLocationResponseViewModel` (note: `[Produces]` attribute misnames this as `Void`) |
| DELETE | `states/{id:int}` | Remove a state | route: `id` | `bool` |
| GET | `cities` | List cities | `LocationsRequestViewModel` (query) | `ListDataSource<LocationsResponseViewModel>` |
| GET | `cities/{id:int}` | Get a city by id | route: `id` | `LocationResponseViewModel` |
| POST | `cities` | Create a city | `ManageLocationRequestViewModel` (body) | `ManageLocationResponseViewModel` |
| PUT | `cities/{id:int}` | Update a city | `UpdateLocationRequestViewModel` (body) + route `id` | `ManageLocationResponseViewModel` |
| DELETE | `cities/{id:int}` | Remove a city | route: `id` | `bool` |

### PaymentsController — Admin-only
`src/Presentation/Api/Areas/Admin/Controllers/PaymentsController.cs` — route `api/v1/admin/payments`

| Verb | Route | Purpose | Request model | Response model |
|---|---|---|---|---|
| GET | `` | List payments (filterable by date range, user, gateway, status) | `PaymentsListRequestViewModel` (query) | `ListDataSource<PaymentsListResponseViewModel>` |
| GET | `export` | Export filtered payments list as an Excel file | `ExportPaymentsListRequestViewModel` (query) | Declared `ApiResponse<string>`; success path actually returns a raw `FileContentResult` (`Payments.xlsx`) — only the error path returns the envelope |

### QuestionsController — Admin-only
`src/Presentation/Api/Areas/Admin/Controllers/QuestionsController.cs` — route `api/v1/admin/questions`

| Verb | Route | Purpose | Request model | Response model |
|---|---|---|---|---|
| GET | `` | List questions | `QuestionsRequestViewModel` (query) | `ListDataSource<QuestionsResponseViewModel>` |
| GET | `{id:long}` | Get a question (with options) by id | route: `id` | `QuestionResponseViewModel` |
| POST | `` | Create a question with options | `ManageQuestionRequestViewModel` (body) | `ManageQuestionResponseViewModel` |
| PUT | `{id:long}` | Update a question and its options | `UpdateQuestionRequestViewModel` (body) + route `id` | `ManageQuestionResponseViewModel` |
| DELETE | `{id:long}` | Remove a question | route: `id` | `bool` |

### SchoolsController — Admin-only
`src/Presentation/Api/Areas/Admin/Controllers/SchoolsController.cs` — route `api/v1/admin/schools`

| Verb | Route | Purpose | Request model | Response model |
|---|---|---|---|---|
| GET | `` | List schools | `SchoolsRequestViewModel` (query) | `ListDataSource<SchoolsResponseViewModel>` |
| GET | `{id:long}` | Get a school by id | route: `id` | `SchoolResponseViewModel` |
| POST | `` | Create a school | `ManageSchoolRequestViewModel` (body) | `ManageSchoolResponseViewModel` |
| PUT | `{id:long}` | Update a school | `UpdateSchoolRequestViewModel` (body) + route `id` | `ManageSchoolResponseViewModel` |
| DELETE | `{id:long}` | Remove a school | route: `id` | `bool` |
| GET | `comments/contributions` | List school-comment contributions | `SchoolCommentContributionListRequestViewModel` (query) | `ListDataSource<SchoolCommentContributionListResponseViewModel>` |
| GET | `comments/contributions/{contributionId:long}` | Get a school-comment contribution for review | route: `contributionId` | `SchoolCommentContributionReviewViewModel` |
| PATCH | `comments/contributions/{contributionId:long}/confirm` | Confirm a school-comment contribution | route: `contributionId` | `bool` |
| PATCH | `comments/contributions/{contributionId:long}/reject` | Reject a school-comment contribution | `RejectSchoolContributionRequestViewModel` (body) + route `contributionId` | `bool` |
| GET | `images/contributions` | List school-image contributions | `SchoolImageContributionListRequestViewModel` (query) | `ListDataSource<SchoolImageContributionListResponseViewModel>` |
| GET | `images/contributions/{contributionId:long}` | Get a school-image contribution for review | route: `contributionId` | `SchoolImageContributionReviewViewModel` |
| PATCH | `images/contributions/{contributionId:long}/confirm` | Confirm a school-image contribution | route: `contributionId` | `bool` |
| PATCH | `images/contributions/{contributionId:long}/reject` | Reject a school-image contribution | `RejectSchoolContributionRequestViewModel` (body) + route `contributionId` | `bool` |
| PATCH | `{schoolId:long}/images/{imageId:long}` | Update tag/default flag of a school image | `ManageSchoolImageRequestViewModel` (body) + route params | `bool` |
| DELETE | `{schoolId:long}/images/{imageId:long}` | Remove a school image | route params | `bool` |
| GET | `images/issues/contributions` | List "remove school image" issue contributions | `RemoveSchoolImageContributionListRequestViewModel` (query) | `ListDataSource<RemoveSchoolImageContributionListResponseViewModel>` |
| GET | `images/issues/contributions/{contributionId:long}` | Get a "remove school image" contribution for review | route: `contributionId` | `RemoveSchoolImageContributionReviewViewModel` |
| PATCH | `images/issues/contributions/{contributionId:long}/confirm` | Confirm a "remove school image" contribution | route: `contributionId` | `bool` |
| PATCH | `images/issues/contributions/{contributionId:long}/reject` | Reject a "remove school image" contribution | `RejectSchoolContributionRequestViewModel` (body) + route `contributionId` | `bool` |
| GET | `contributions` | List school-edit contributions | `SchoolContributionListRequestViewModel` (query) | `ListDataSource<SchoolContributionListResponseViewModel>` |
| GET | `contributions/{contributionId:long}` | Get a school-edit contribution (old vs. new values) for review | route: `contributionId` | `SchoolContributionReviewViewModel` |
| PATCH | `contributions/{contributionId:long}/confirm` | Confirm a school-edit contribution | `ConfirmSchoolContributionRequestViewModel` (body) + route `contributionId` | `bool` |
| PATCH | `contributions/{contributionId:long}/reject` | Reject a school-edit contribution | `RejectSchoolContributionRequestViewModel` (body) + route `contributionId` | `bool` |
| GET | `issues/contributions` | List school-issue contributions | `SchoolIssuesContributionListRequestViewModel` (query) | `ListDataSource<SchoolIssuesContributionReviewResponseViewModel>` |
| PATCH | `issues/contributions/{contributionId:long}/confirm` | Confirm a school-issue contribution | route: `contributionId` | `bool` |
| PATCH | `issues/contributions/{contributionId:long}/reject` | Reject a school-issue contribution | `RejectSchoolContributionRequestViewModel` (body) + route `contributionId` | `bool` |
| GET | `site-maps` | List school sitemap entries | `SiteMapListRequestViewModel` (query) | `ListDataSource<SiteMapListResponseViewModel>` |
| POST | `{schoolId:long}/site-maps` | Create a sitemap entry for a school | `ManageSiteMapRequestViewModel` (body) + route `schoolId` | `ManageSiteMapResponseViewModel` |
| PUT | `{schoolId:long}/site-maps/{id:long}` | Update a school sitemap entry | `ManageSiteMapRequestViewModel` (body) + route params | `ManageSiteMapResponseViewModel` |
| DELETE | `{schoolId:long}/site-maps/{id:long}` | Remove a school sitemap entry | route params | `bool` |

### SubjectsController — Admin-only
`src/Presentation/Api/Areas/Admin/Controllers/SubjectsController.cs` — route `api/v1/admin/subjects`

| Verb | Route | Purpose | Request model | Response model |
|---|---|---|---|---|
| GET | `` | List subjects (optionally filtered by grade) | `SubjectsRequestViewModel` (query) | `ListDataSource<SubjectsResponseViewModel>` |
| GET | `{id:int}` | Get a subject by id | route: `id` | `SubjectResponseViewModel` |
| POST | `` | Create a subject | `ManageSubjectRequestViewModel` (body) | `ManageSubjectResponseViewModel` |
| PUT | `{id:int}` | Update a subject | `UpdateSubjectRequestViewModel` (body) + route `id` | `ManageSubjectResponseViewModel` |
| DELETE | `{id:int}` | Remove a subject | route: `id` | `bool` |

### SubscriptionsController — Admin-only
`src/Presentation/Api/Areas/Admin/Controllers/SubscriptionsController.cs` — route `api/v1/admin/subscriptions`. Plans no longer carry `price`/`currency`/`point` directly (see `docs/business/subscriptions.md`) — those moved to the `prices` and `plans/{id}/features` endpoints below.

| Verb | Route | Purpose | Request model | Response model |
|---|---|---|---|---|
| GET | `plans` | List subscription plans (with prices + features) | `SubscriptionPlansRequestViewModel` (query) | `ListDataSource<SubscriptionPlanResponseViewModel>` |
| GET | `plans/{id:long}` | Get a subscription plan by id | route: `id` | `SubscriptionPlanResponseViewModel` |
| POST | `plans` | Create a subscription plan | `ManageSubscriptionPlanRequestViewModel` (body) | `ManageSubscriptionPlanResponseViewModel` |
| PUT | `plans/{id:long}` | Update a subscription plan | `ManageSubscriptionPlanRequestViewModel` (body) + route `id` | `ManageSubscriptionPlanResponseViewModel` |
| DELETE | `plans/{id:long}` | Remove a subscription plan; fails if any `UserSubscription` ever referenced it | route: `id` | `bool` |
| GET | `features` | List the feature catalog | `FeaturesRequestViewModel` (query) | `ListDataSource<FeatureResponseViewModel>` |
| POST | `features` | Create a feature | `ManageFeatureRequestViewModel` (body) | `ManageFeatureResponseViewModel` |
| PUT | `features/{id:int}` | Update a feature | `ManageFeatureRequestViewModel` (body) + route `id` | `ManageFeatureResponseViewModel` |
| DELETE | `features/{id:int}` | Remove a feature | route: `id` | `bool` |
| GET | `plans/{id:long}/features` | Get a plan's feature limits | route: `id` | `IEnumerable<PlanFeatureViewModel>` |
| PUT | `plans/{id:long}/features` | Replace a plan's entire feature/limit set | route: `id` + `SetPlanFeaturesRequestViewModel` (body) | `bool` |
| GET | `prices` | List plan prices (paged) | `SubscriptionPlanPricesRequestViewModel` (query) | `ListDataSource<SubscriptionPlanPriceResponseViewModel>` |
| POST | `prices` | Create a plan price (`countryCode: null` = the plan's global default) | `ManageSubscriptionPlanPriceRequestViewModel` (body) | `ManageSubscriptionPlanPriceResponseViewModel` |
| PUT | `prices/{id:long}` | Update a plan price | `ManageSubscriptionPlanPriceRequestViewModel` (body) + route `id` | `ManageSubscriptionPlanPriceResponseViewModel` |
| DELETE | `prices/{id:long}` | Remove a plan price | route: `id` | `bool` |
| GET | `gateway-mappings` | List gateway Product/Price mappings (paged) | `GatewayMappingsRequestViewModel` (query) | `ListDataSource<GatewayMappingResponseViewModel>` |
| POST | `gateway-mappings` | Create a gateway mapping — written now, not yet read by anything until native recurring billing ships (see `docs/business/subscriptions.md`) | `ManageGatewayMappingRequestViewModel` (body) | `ManageGatewayMappingResponseViewModel` |
| PUT | `gateway-mappings/{id:long}` | Update a gateway mapping | `ManageGatewayMappingRequestViewModel` (body) + route `id` | `ManageGatewayMappingResponseViewModel` |
| DELETE | `gateway-mappings/{id:long}` | Remove a gateway mapping | route: `id` | `bool` |

### TagsController — Admin-only
`src/Presentation/Api/Areas/Admin/Controllers/TagsController.cs` — route `api/v1/admin/tags`

| Verb | Route | Purpose | Request model | Response model |
|---|---|---|---|---|
| GET | `` | List tags (optionally filtered by tag type) | `TagsRequestViewModel` (query) | `ListDataSource<TagsResponseViewModel>` |
| GET | `{id:long}` | Get a tag by id | route: `id` | `TagResponseViewModel` |
| POST | `` | Create a tag | `ManageTagRequestViewModel` (body) | `ManageTagResponseViewModel` |
| PUT | `{id:long}` | Update a tag | `UpdateTagRequestViewModel` (body) + route `id` | `ManageTagResponseViewModel` |
| DELETE | `{id:long}` | Remove a tag | route: `id` | `bool` |

### TicketsController — Admin-only
`src/Presentation/Api/Areas/Admin/Controllers/TicketsController.cs` — route `api/v1/admin/tickets`

| Verb | Route | Purpose | Request model | Response model |
|---|---|---|---|---|
| GET | `` | List tickets | `TicketsRequestViewModel` (query) | `ListDataSource<TicketsResponseViewModel>` |
| POST | `` | Send a new ticket/email to a user, optional attachment | `SendTicketRequestViewModel` (multipart form) | `ManageTicketResponseViewModel` |
| GET | `{id:long}` | Get ticket details; marks read-by-admin | route: `id` | `TicketResponseViewModel` |
| GET | `{id:long}/replys` | Get all replies for a ticket; marks replies read-by-admin | route: `id` | `IEnumerable<TicketReplyResponseViewModel>` |
| POST | `{id:long}/replys` | Reply to a ticket, optional attachment | `ReplyTicketByAdminRequestViewModel` (multipart form) + route `id` | `Void` (no data) |
| PATCH | `{id:long}/toggle` | Toggle a ticket's "read by admin" flag | route: `id` | `bool` |
| DELETE | `{id:long}` | Remove a ticket | route: `id` | `bool` |

### TopicsController — Admin-only
`src/Presentation/Api/Areas/Admin/Controllers/TopicsController.cs` — route `api/v1/admin/topics`

| Verb | Route | Purpose | Request model | Response model |
|---|---|---|---|---|
| GET | `` | List topics (optionally filtered by subject) | `TopicsRequestViewModel` (query) | `ListDataSource<TopicsResponseViewModel>` |
| GET | `{id:int}` | Get a topic by id | route: `id` | `TopicResponseViewModel` |
| POST | `` | Create a topic | `ManageTopicRequestViewModel` (body) | `ManageTopicResponseViewModel` |
| PUT | `{id:int}` | Update a topic | `UpdateTopicRequestViewModel` (body) + route `id` | `ManageTopicResponseViewModel` |
| DELETE | `{id:int}` | Remove a topic | route: `id` | `bool` |

### TransactionsController — Admin-only
`src/Presentation/Api/Areas/Admin/Controllers/TransactionsController.cs` — route `api/v1/admin/transactions`

| Verb | Route | Purpose | Request model | Response model |
|---|---|---|---|---|
| GET | `` | List transactions (filterable by debit flag, date range, user, identifier, type) | `TransactionsListRequestViewModel` (query) | `ListDataSource<TransactionsListResponseViewModel>` |
| POST | `` | Create a manual admin transaction (increase/decrease a user's balance); emails the user | `CreateTransactionRequestViewModel` (body) | `CreateTransactionResponseViewModel` |

---

## Finance controller (`src/Presentation/Api/Areas/Finance/Controllers/`)

Base route: `api/v{version:apiVersion}/[area]/[controller]` → `api/v1/finance/payments`.
Gated by class-level `[Permission(Roles = [nameof(Role.Finance)])]` — **`Role.Finance`, not
`Role.Admin`** (see `authentication.md`). This is the only controller in this area.

### PaymentsController — Finance-only
`src/Presentation/Api/Areas/Finance/Controllers/PaymentsController.cs`

| Verb | Route | Purpose | Request model | Response model |
|---|---|---|---|---|
| GET | `summary` | Daily payments summary (paid/pending/failed amounts and counts) over a date range, filterable by user, gateway, status, currency | `PaymentsSummaryRequestViewModel` (query) | `IEnumerable<PaymentsSummaryResponseViewModel>` |

---

## Notable attribute/behavior mismatches found while cataloging

A few actions have a `[Produces(...)]` attribute that doesn't match what the method body actually
returns. These are source-level inconsistencies (not doc errors) — the table entries above already
report the *actual* runtime response type, with the mismatch called out:

- `GamesController.ExamPoints` — declares `TestTimeQuizResponseViewModel`, returns `ExamPointsResponseViewModel`.
- `ExamsController.Export` — declares generic `IActionResult`, actually branches between `ApiResponse<Void>` and a raw file stream.
- `FilesController.GetFile` — declares `ApiResponse<string>`, can also short-circuit to an HTTP redirect.
- `SchoolsController` (public) `RequestRemoveSchoolImage`-style action — declares `bool`, returns `RemoveSchoolImageContributionResponseViewModel`.
- `LocationsController` (Admin) `UpdateState` — declares `Void`, returns `ManageLocationResponseViewModel`.
- `PaymentsController` (Admin) `Export` — declares `ApiResponse<string>`, success path returns a raw `FileContentResult` (xlsx download).

Also flagged for review (not mismatches, but noteworthy deviations from the codebase's own
conventions): `HomeController` has no `[Route]`/`[ApiVersion]`/auth attribute at all;
`ExamsController` omits `[ApiVersion("1.0")]`; and
`VotingPowersController`'s bulk-import `POST` is fully anonymous and relies solely on an in-body
signature check rather than any `[Authorize]`-derived attribute.

`GamesController` was the first controller in the solution to carry two `[ApiVersion]`
attributes (`spends` needed a v1-compatible bare-`bool` response alongside a richer v2
response for the new subscription-quota upsell — see `docs/business/subscriptions.md`).
Every other action on it has no `[MapToApiVersion]` and so is served under both versions
unchanged.
