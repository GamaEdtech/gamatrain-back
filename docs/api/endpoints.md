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

### PostsController
`src/Presentation/Api/Controllers/PostsController.cs` — route `api/v1/posts`, class-level `[Permission(policy: null)]` (User by default; several actions override with `[AllowAnonymous]`)

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| GET | `` | List published (`Confirmed`, publish date reached) posts, filterable by tag/visibility/date/title. Newest **update** first by default (since 2026-11-09): the later of the publish date and the last change; each item carries that as `lastUpdateDate`. A `SortFilter` overrides it | Anonymous | `PostsRequestViewModel` (query) | `ListDataSource<PostsResponseViewModel>` |
| GET | `random` | Get a random set of published posts | Anonymous | `RandomPostsRequestViewModel` (query) | `ListDataSource<PostsResponseViewModel>` |
| GET | `{postId:long}` | Get a single published (`Confirmed`) post; increments view count in background | Anonymous | route: `postId` | `PostResponseViewModel` |
| DELETE | `{postId:long}` | Remove a post (only if caller is its creator) | User | route: `postId` | `bool` |
| PATCH | `{postId:long}/like` | Like a post | User | route: `postId` | `bool` |
| PATCH | `{postId:long}/dislike` | Dislike a post | User | route: `postId` | `bool` |
| GET | `slugs/generate` | Generate a unique slug from a title | User | query: `title` | `string` |
| GET | `slugs/validate` | Check whether a slug is available | User | query: `slug` | `bool` |
| GET | `mine` | List the caller's own posts (any status). `Status` query param optional — omitting it returns every status (declared `Status?` so ASP.NET Core doesn't make it implicitly required) | User | `MyPostsRequestViewModel` (query) | `ListDataSource<ManagedPostsResponseViewModel>` |
| GET | `mine/{postId:long}` | Get one of the caller's own posts for editing, any status, incl. every localized value, `Status` and `RejectionComment` | User | route: `postId` | `PostEditResponseViewModel` |
| POST | `` | Create a post. `Draft=true` saves it as `Draft`; otherwise it enters `Review` (or is auto-confirmed via `SystemClaim.AutoConfirmPost` / `AutoConfirmPosts`) | User | `CreatePostRequestViewModel` (form) | `ManagePostResponseViewModel` |
| PUT | `{postId:long}` | Edit one's own post (must be creator). Any owner edit sends it back to `Draft`/`Review` — a previously `Confirmed` post is hidden from the public API until re-approved | User | `UpdatePostRequestViewModel` (form) + route `postId` | `ManagePostResponseViewModel` |
| GET | `{postId:long}/comments` | List comments on a post | Anonymous | `PostCommentsRequestViewModel` (query) + route `postId` | `ListDataSource<PostCommentsResponseViewModel>` |
| POST | `{postId:long}/comments` | Create a comment on a post (captcha-verified) | User | `ManagePostCommentRequestViewModel` (body) + route `postId` | `ManagePostCommentResponseViewModel` |
| PATCH | `{postId:long}/comments/{commentId:long}/like` | Like a comment | User | route params | `bool` |
| PATCH | `{postId:long}/comments/{commentId:long}/dislike` | Dislike a comment | User | route params | `bool` |

### BoardsController
`src/Presentation/Api/Controllers/BoardsController.cs` — class-level `[Permission(policy: null)]` + `[AllowAnonymous]` (whole controller anonymous)

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| GET | `` | List all education boards (`[ResponseCache(Duration=300)]`) | Anonymous | none | `IEnumerable<BoardsListResponseViewModel>` |

### CommissionsController
`src/Presentation/Api/Controllers/CommissionsController.cs` — class-level `[Permission(policy: null)]` (User). Deliberately separate from `DownloadsController` — commissions are earned via a `Reason` (`ContentDownload`, and since 2026-10-21 `ReferralSubscription` for teacher invite earnings, see `docs/business/referrals.md`), and `Reason`/`Source` are kept apart specifically so a future commission event doesn't have to be shaped as a "download" at the API surface. See `docs/business/content-delivery.md`.

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| GET | `` | Report of the caller's own accrued `ContentOwnerCommission` rows (filterable by `startDate`/`endDate`, and `reason`: `ContentDownload`/`ReferralSubscription`; a referral row has `source`/`contentType`/`externalContentId`/`externalFileType` null), forced to `OwnerUserIdEqualsSpecification(User.UserId())` — a caller can never see another owner's commissions this way. Every accrual, paid out or not (see `balance`) | User | `ContentOwnerCommissionsListRequestViewModel` (query) | `ListDataSource<ContentOwnerCommissionListResponseViewModel>` |
| GET | `statistics` | Caller's own commission accrual, bucketed by day-of-week/month (`Statistics` — `AmountUsd` + `Points` per bucket, same shape as `TransactionsController`'s `statistics`), plus `TotalAmountUsd`/`TotalPoints` — the caller's lifetime commission balance, deliberately unaffected by the `startDate`/`endDate`/`period` filter | User | `CommissionStatisticsRequestViewModel` (query) | `CommissionStatisticsResponseViewModel` |
| GET | `balance` | Caller's commission balance: earned, paid out, reserved by the open payout request, available (whole cents), the payout threshold, and the open request's id | User | — | `CommissionBalanceResponseViewModel` |
| GET | `payouts` | Caller's own payout requests, newest first (optional `status` filter) | User | `CommissionPayoutsListRequestViewModel` (query) | `ListDataSource<CommissionPayoutResponseViewModel>` |
| POST | `payouts` | Request a payout: `amountUsd` (optional, default the whole available balance; ≥ threshold, whole cents), `method` (`StripeConnect` default, needs `payout-account` enabled; or `Manual`), `destination` (Manual only). One open request at a time. Returns the request id | User | `RequestCommissionPayoutRequestViewModel` (body) | `long` |
| PATCH | `payouts/{payoutId:long}/cancel` | Cancel the caller's own request while Pending | User | route: `payoutId` | `bool` |
| GET | `payout-account` | Caller's Stripe payout account, read live from Stripe: `hasAccount`, `country`, `detailsSubmitted`, `payoutsEnabled` | User | — | `PayoutAccountResponseViewModel` |
| POST | `payout-account/onboarding` | Start/continue Stripe payout setup: creates the Stripe Express account on first use (`country`, ISO alpha-2) and returns a Stripe onboarding URL to redirect to. `returnUrl` must be on one of our domains (`CorsUrls`); Stripe comes back with `?stripe=return` or `?stripe=refresh` | User | `PayoutOnboardingRequestViewModel` (body) | `string` |

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

### DownloadsController
`src/Presentation/Api/Controllers/DownloadsController.cs` — per-action `[Permission(policy: null)]` (User). Resolves downloadable content from external sources (gama-api's legacy PastPaper/Multimedia/Exam content today) and combines the source lookup, the downloader's charge, and the content owner's commission accrual into one call — see `docs/business/content-delivery.md`. Commission reporting itself lives on `CommissionsController`, not here.

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| POST | `` | Resolve a download URL; `contentType` (`DownloadContentType`: `PastPaper`/`Multimedia`/`Exam` only — a dedicated 3-member enum, not the broader `ContentType` used elsewhere) selects which gama-api endpoint is called. `PastPaper` requires `fileType` (`pdf`/`word`/`answer`/`extra`, `extraId` only for `extra`) and charges the downloader (quota-then-points) unless gama-api reports the download as already paid, accruing owner commission on success; `Multimedia`/`Exam` are unconditionally free (gama-api reports no price for either). On a quota-exhausted charge failure, `upgradeSuggestions[]` carries every plan+interval offering the failed feature, each flagged `isCurrent`/`canUpgrade` since 2026-08-16, not filtered/capped — see `docs/business/subscriptions.md`. If gama-api's own download call fails *after* the charge already succeeded (its detail/price-check call and its download call are separate; the token can be rejected in between), the charge is refunded (quota or wallet points, whichever paid) rather than left charged for nothing (fixed 2026-09-03) — see `docs/business/content-delivery.md`, "Charged but never delivered...". **Deviation from this API's usual "always 200" convention:** if gama-api specifically rejects the caller's forwarded legacy token (401/403) for this call, this endpoint returns a real HTTP `401` (not the usual `200`) — same narrow exception `identities/dashboard` already uses for the identical failure shape (fixed 2026-09-03) | User (requires the caller's `Authorization` header to carry their gama-api legacy JWT — see `docs/api/authentication.md`) | `DownloadContentRequestViewModel` (body) | `DownloadContentResponseViewModel` |
| GET | `papers/{id:long}/owned` | Which of a past paper's `pdf`/`word`/`answer` files the caller already owns (since 2026-11-02), so the paper page can mark them "Owned" before a click. Once `Migration:WriteRoutes:PastPapers.Downloads` is `Native`, read from `ContentPurchases` (every file for the paper's owner and admins), and `POST` serves a past paper from this backend (signed private link, charged once); see `docs/business/past-papers.md`, "Downloads". Read from gama-api's own per-user `paid` flags on its side-effect-free `GET /tests/{id}` — the same flags `POST` above trusts before charging. gama-api reports no flag for the `extra` files. Per user, so `Cache-Control: private,no-store`; the paper's public data comes from the cached `Search/papers/{id}` instead. Same real-`401` exception as `POST` when gama-api rejects the forwarded legacy token | User (gama-api legacy JWT, as `POST`) | route: `id` | `PaperOwnershipResponseViewModel` (`pdf`, `word`, `answer`) |

### ExamBuilderController
`src/Presentation/Api/Controllers/ExamBuilderController.cs` — route `api/v1/examBuilder`, `[Permission(Roles = [Teacher, Admin])]` (since 2026-11-23). A teacher building exams over REST: the website's test maker and the exam bots. Everything acts for the signed-in user (`IExamBuilderService`, through `IMigrationRouter` as `Exams.TestMaker`; native once its routes are `Native`). Per user, `Cache-Control: private,no-store`. See `docs/business/exams-migration.md`, "Exam builder API".

| Verb | Route | Purpose | Request | Response |
|---|---|---|---|---|
| GET | `current` | The caller's draft (one at a time), or null | none | `long?` |
| GET | `exams` | The caller's exams, newest first, with `status` and `owner` | query: `statuses[]`, `page`, `pageSize` (≤ 50) | `ListDataSource<ExamBuilderExamViewModel>` |
| GET | `exams/{id}` | One exam (theirs, or a public one) | none | `ExamBuilderExamViewModel` |
| POST | `exams` | Create the caller's draft (`alreadyInProgress` while one exists) | `ExamBuilderExamRequestViewModel` (body) | `long` (id) |
| PUT | `exams/{id}` | Change the caller's exam | `ExamBuilderExamRequestViewModel` (body) | `long` |
| DELETE | `exams/{id}` | Hide the caller's exam | none | `Void` |
| GET | `exams/{id}/questions` | The exam's questions in order | none | `IEnumerable<ExamImportDraftQuestionDto>` |
| PUT | `exams/{id}/questions` | Replace the question list, in order | `ExamBuilderQuestionsRequestViewModel` (body) | `Void` |
| POST | `exams/{id}/publish` | Publish (status 7, public at once) | none | `Void` |
| GET | `questions` | The bank: published questions and the caller's own, with the correct option | query: `boardId`, `gradeId`, `subjectId`, `topicId`, `page`, `pageSize` (≤ 100) | `ListDataSource<ExamBankQuestionDto>` |
| POST | `questions` | Add a question (the caller's, waiting) | `ExamBuilderQuestionRequestViewModel` (body) | `long` |
| PUT | `questions/{id}` | Change the caller's question | `ExamBuilderQuestionRequestViewModel` (body) | `long` |
| DELETE | `questions/{id}` | Delete the caller's question (`inUse` when a published exam uses it or it has answers) | none | `Void` |
| POST | `uploads` | Store a question image (PNG/JPEG/GIF/WebP ≤ 5 MB); the key for a question's image fields | multipart `file` | `string` |

### ExamsController
`src/Presentation/Api/Controllers/ExamsController.cs` — class-level `[Permission(policy: null)]` (User). **Deviation:** no `[ApiVersion]` attribute (only `[ApiController]`).

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| GET | `{id:long}` | Everything the public exam page shows, in one call (added 2026-10-24): gama-api's `exams/{id}` data unchanged (`exam`, snake_case keys), the first 5 questions in exam order **without their answers** (`sampleQuestions`), and the past paper it was made from (`paper`). Samples and paper are cached per exam for 6 hours; the exam itself is live on every call. A failed sample or paper is left out, never failing the call. See `docs/business/exams-and-content.md`, "Public exam page data" | Anonymous (a token is optional, forwarded to gama-api's `exams/{id}` only, for the caller's own fields like `examUserData`) | Route `id` | `ExamPageResponseViewModel`: `exam` (object), `sampleQuestions[]` of `{ id, code, question, image, options[]: { key, text, image } }` (`key` is the option's number 1-4, as the correct answer names it) (link a question to `/test/{code}`), `paper` (`{ id, title, slug, isPastPaper }` or null) |
| GET | `export/prices` | The exam's export price per paid format for the caller (added 2026-09-27) | Anonymous (since 2026-10-14; a valid token is still read, for `purchased`, and forwarded to gama-api's `exams/{id}` for the question count) | `ExportPricesRequestViewModel` (query: `id`) | `ExportPricesResponseViewModel`: `questionCount`, `items[]` of `{ fileType: "Pdf"|"Word"|"PowerPoint", points, purchased }` |
| GET | `{id}/result` | The exam results page in one call (added 2026-10-26): the caller's gama-api `exams/result/{id}` data as gama-api sends it, plus their AI analysis. The frontend loads this instead of gama-api's endpoint, so a saved analysis shows without asking again. Never charges. It is also where a finished exam is recorded (`ExamSubmission`) and its points given, the first time the caller opens it (since 2026-11-15; `games/exams/points` was removed) | User (the `Authorization` header must carry the caller's gama-api legacy JWT, forwarded to gama-api) | none (exam id in the route) | `ExamResultPageResponseViewModel`: `result` (gama-api's data, unchanged, snake_case keys), `analysis` (`id`/`result` of the saved analysis or null, `points` = what getting one costs now, 0 when already paid for, `purchased`); `analysis` is null when it couldn't be read, the result still comes back. `succeeded: false` with gama-api's error when gama-api refuses the result |
| POST | `{id}/end` | Submit the caller's exam (added 2026-11-15): passes the body to gama-api's `exams/end/{id}` unchanged, then records the exam (`ExamSubmission`) with its points and the countdown's time spent, so the result shows the time actually spent. The exam page calls this instead of gama-api | User (the `Authorization` header must carry the caller's gama-api legacy JWT, forwarded to gama-api) | `ExamEndRequestViewModel` (body): `startID`, `answers` (both passed through), `timeSpentSeconds` | `ExamEndResponseViewModel`: `id` = gama-api's result id, also set when gama-api refuses an exam already submitted (`succeeded: false`) |
| GET | `{id:long}/start` | Start the caller's attempt at exam `id`, or resume their unfinished one (since 2026-11-17; gama-api's `exams/start/{id}` data): `startID`, `exam`, the questions without their answers (`tests`) and `remainedSeconds`; `alreadyParticipated` (with the attempt id in `value`) once submitted. A write: follows `Migration:WriteRoutes:Exams.Games`. `private,no-store` | User | route | `JsonObject` |
| GET | `results` | The caller's own exam results, newest first (since 2026-11-17; gama-api's `exams/results`): `num`, `list` | User | query: `page`, `perpage` | `JsonObject` |
| GET | `{id:long}/participants` | Who took exam `id`, for its owner only (since 2026-11-17; gama-api's `exams/participants/{id}`): `num`, `list`, `info.exam` | User | query: `page`, `perpage`, `status` | `JsonObject` |
| GET | `{id:long}/reviews` | The exam's reviews: average, count, 1-5 breakdown and comments (newest first) of the shown ones; with a token also the caller's own review and `canReview` (since 2026-11-23) | Anonymous (token optional) | query: `page`, `pageSize` (≤ 50) | `ExamReviewsResponseViewModel` |
| POST | `{id:long}/reviews` | Save the caller's review (rating 1-5, optional comment), public at once; only after finishing the exam (`notParticipated`); can be changed for 30 minutes after posting (`editWindowClosed`), no delete | User | `SaveExamReviewRequestViewModel` (body) | `ExamReviewViewModel` |
| POST | `{id}/analysis` | The caller's AI analysis of their own result for exam `id` (added 2026-10-26). Paid once per user+exam (admin-set `examAnalysisPoints`, default 100, from `ExamAnalysis` quota then points); returned free after that. The backend reads the result (questions, options, correct and chosen answers, score, time, rank) from gama-api's `exams/result/{id}` with the caller's token and asks `IAiService` for plain-text feedback. If the AI fails, the charge is refunded. See `docs/business/exams-and-content.md`, "Paid exam analysis" | User (the `Authorization` header must carry the caller's gama-api legacy JWT, forwarded to gama-api) | none (exam id in the route, no body) | `ExamAnalysisResponseViewModel`: `id`, `examId`, `result` (the analysis text), `points`; plus `paidBy`/`remainingQuota` on a new charge, or `reason`/`currentPlan*`/`upgradeSuggestions`/`availableBillingIntervals` when the charge is refused (same shape as `POST downloads`). `succeeded: false` with an error when the result can't be read or has no questions (nothing charged) |
| GET | `export` | Export an exam to a file. **Paid since 2026-09-27** (Pdf/Word/PowerPoint; the thumbnail is free): question count x the format's multiplier, from `ExamDownload` quota then points, once per user+exam+format. On success the file carries `X-Export-Points` (0 when free/already bought), `X-Export-Paid-By` (`SubscriptionQuota`/`Points`) and `X-Export-Already-Purchased` headers (exposed via CORS, with `Content-Disposition`). `Content-Disposition` is `attachment; filename="<ASCII fallback>"; filename*=UTF-8''<title>.<ext>` (RFC 6266, via `FileContentResult.FileDownloadName`); until 2026-10-09 a non-ASCII title (e.g. the em dash in MCP-imported titles) came out RFC 2047-encoded (`=?utf-8?B?...?=`), which browsers saved literally, without the extension; a refused charge returns `ApiResponse<DownloadContentResponseViewModel>` (same `reason`/`upgradeSuggestions` shape as `POST downloads`) | User (requires the caller's `Authorization` header to carry their gama-api legacy JWT, forwarded to gama-api's `exams/{id}`/`examTests` — see `docs/api/authentication.md`; the export includes a filled Answer Key for every caller; `fileType=Thumbnail` returns a 496x792 WebP of the Pdf's first page (`image/webp`); no longer a separate `SecretKey` header, see below) | `ExportExamRequestViewModel` (query) | Declared `IActionResult`; error path returns `ApiResponse<Void>`, success path returns a raw `FileContentResult` (binary file), not the envelope |

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

### EditorImagesController
`src/Presentation/Api/Controllers/EditorImagesController.cs` — class-level `[Permission(policy: null)]` (User). See
`docs/architecture/cross-cutting-concerns.md`, "Rich editor images".

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| POST | `` | Upload one image a rich editor inserts (JPG/PNG/GIF/WebP by content, at most 5 MB, stored as WebP except a GIF, metadata removed, 60 an hour per user) into the public `EditorImage` container; answers its CDN address | User | form: `file` | `EditorImageResponseViewModel` (`url`) |

### GamesController
`src/Presentation/Api/Controllers/GamesController.cs` — **no class-level auth attribute** (each action carries its own). Now carries `[ApiVersion("1.0")]` + `[ApiVersion("2.0")]` (added for the `spends` split below) — every other action is unmapped and so still serves both versions unchanged.

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| GET | `easter-egg/fortune-wheel` | Spin the "fortune wheel" easter egg, awarding coins | **ApiKey** | none | `CoinsResponseViewModel` |
| POST | `easter-egg/points` | Award points for an easter-egg interaction | User | `EasterEggPointsRequestViewModel` (body) | `EasterEggPointsResponseViewModel` |
| POST | `test-time` | Record a "test time" quiz answer/timing | User | `TestTimeQuizRequestViewModel` (body) | `TestTimeQuizResponseViewModel` |
| POST | `spends` (v1.0) | Spend on pastpaper/test download — tries the caller's subscription quota first (see `docs/business/subscriptions.md`), falls back to wallet points unchanged | User | `SpendPointsRequestViewModel` (body) | `bool` (unchanged wire shape) |
| POST | `spends` (v2.0) | Same action as v1, richer response for clients that want to show which path paid and any upgrade upsell | User | `SpendPointsRequestViewModel` (body) | `SpendPointsResponseViewModel` (`spent`, `paidBy`, `remainingQuota`, `upgradeSuggestions[]` - every plan+interval offering the failed feature, each flagged `isCurrent`/`canUpgrade` since 2026-08-16, not filtered/capped - see `docs/business/subscriptions.md`) |

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
| POST | `register/code` | Sign-up step 1: email a 6-digit code to an address with no account (`already has an account` otherwise); a new code once a minute. Answers the address, masked | Anonymous | `EmailCodeRequestViewModel` (body: `email`) | `string` |
| POST | `register/code/verify` | Sign-up step 2: check the code before the password (doesn't use it up; wrong tries count) | Anonymous | `EmailCodeCheckRequestViewModel` (body: `email`, `code`) | `Void` |
| POST | `register` | Sign-up step 3: create the local account with a confirmed email, after checking the code again (used up). Queues the welcome email and signs the person in. Optional `referralCode` (teacher invite code) links the new account to its inviter (`docs/business/referrals.md`) | Anonymous | `RegistrationRequestViewModel` (body: `email`, `code`, `password`, `confirmPassword`, names, `referralCode`) | `GenerateTokenResponseViewModel` |
| POST | `login/code` | Sign-in with an emailed code, step 1 (gama-api's `loginByOTP`): email a 6-digit sign-in code to an account's address. The website asks for it after a wrong password. Answers the address, masked | Anonymous | `EmailCodeRequestViewModel` (body) | `string` |
| POST | `tokens/code` | Sign-in with an emailed code, step 2: email + code instead of the password (code used up); confirms the email and clears a password lockout | Anonymous | `EmailCodeCheckRequestViewModel` (body) | `GenerateTokenResponseViewModel` |
| POST | `password/reset-code` | Forgotten password step 1: email a 6-digit code to an account's address. Answers the address, masked | Anonymous | `EmailCodeRequestViewModel` (body) | `string` |
| POST | `password/reset-code/verify` | Forgotten password step 2: check the code (doesn't use it up) | Anonymous | `EmailCodeCheckRequestViewModel` (body) | `Void` |
| POST | `password/reset` | Forgotten password step 3: the code again and the new password; confirms the email, clears a lockout, ends the user's other sessions and signs them in | Anonymous | `PasswordResetWithCodeRequestViewModel` (body: `email`, `code`, `password`, `confirmPassword`) | `GenerateTokenResponseViewModel` |
| GET | `logout` | Sign the current user out | User | none | `Void` (no data) |
| PUT | `password` | Change current user's password | User | `ChangePasswordRequestViewModel` (body) | `Void` (no data) |
| POST | `tokens` | Authenticate and issue an opaque bearer token. The account is found by email (then user name). A failure answers "Wrong email or password." with `reference: wrongPassword` (also for an unknown email), or `lockedOut` / `disabled`. Only this backend's password: gama-api is never asked (a password only gama-api knows signs in with `login/code` instead) | Anonymous | `GenerateTokenRequestViewModel` (body) | `GenerateTokenResponseViewModel` |
| POST | `tokens/google` | Authenticate via Google OAuth and issue a bearer token. Optional `referralCode`, used only when this sign-in creates the account | Anonymous | `GenerateTokenWithGoogleRequestViewModel` (body) | `GenerateTokenResponseViewModel` |
| POST | `tokens/revoke` | Revoke current user's API access token | User | none | `RevokeTokenResponseViewModel` |
| GET | `authenticated` | Check whether the current request is authenticated | Anonymous (class default overridden) | none | `bool` |
| GET | `permissions` | The caller's own role-gated (area) endpoints as `Area.Controller.Action` keys, e.g. `Admin.Posts.GetPosts`: every endpoint their roles open plus each endpoint granted to them one by one. For the admin panel to show only the menus/pages the caller can open; each endpoint still checks its own permission. Added 2026-11-09, see `docs/business/identity-and-access.md`, "Delegated admin permissions" | User | none | `string[]` |
| GET | `profiles` | Get current user's own profile settings | User | none | `ProfileSettingsResponseViewModel` |
| GET | `profiles/progress` | The caller's own progress for the profile page: profile `level`/`percent`/`nextLevel`, `quests` (the dashboard's field codes, each with its percent), `points` and leaderboard `rank`, practice `streak` (UTC days), and their `school`'s counts and shared review goal (no other member named). Read-only; see `docs/business/identity-and-access.md`, "Profile progress" | User | none | `ProfileProgressResponseViewModel` |
| GET | `dashboard` | Get current user's dashboard payload, built entirely from this backend's own tables (never calls gama-api): `user` (profile, `roles`, `points`, `board`/`grade`, `subscription`), `profileCompletion`, `unreadMessages`, `stats.test.total`/`stats.exam.total` (papers and exams the user owns) and `examSuggestions` (the published online exams of each subject of the user's grade, and how many the user finished). See `docs/business/identity-and-access.md`'s "User dashboard" | User | none | `DashboardResponseViewModel` |
| GET | `profiles/list` | Search/list public **teacher** profiles by name/skill (only `Public` profiles of users in `Role.Teacher` with a first or last name; students, other roles and nameless profiles are never listed) | Anonymous | `PublicProfileListRequestViewModel` (query) | `ListDataSource<PublicProfileListResponseViewModel>` |
| GET | `profiles/{handle}` | Get a public profile by handle | Anonymous | route: `handle` | `PublicProfileResponseViewModel` |
| PUT | `profiles` | Update current user's profile settings | User | `ProfileSettingsRequestViewModel` (body) | `bool` |
| PUT | `profiles/role` | Set the current user's own type, `Teacher` or `Student`: `Group` 5/6 and the matching role, on this backend only (never gama-api; replaced `legacy-auth/group`, removed 2026-11-23). See `docs/business/identity-and-access.md`'s "User type" | User | `UpdateUserRoleRequestViewModel` (body: `role`) | none |
| PATCH | `profiles/avatars` | Set/update current user's avatar | User | `ManageAvatarRequestViewModel` (body) | `bool` |
| DELETE | `profiles/avatars` | Remove current user's avatar | User | none | `bool` |
| GET | `leader-board` | Get top 100 users by points, filterable by board/grade/location/date | Anonymous | `Top100UsersRequestViewModel` (query, nullable) | `IEnumerable<UserPointsViewModel>` |
| POST | `profiles/delete-code` | Email the current user a one-time code (10 min) to confirm deleting the account; resend once a minute | User | none | `string` (masked email it went to) |
| DELETE | `profiles` | Schedule deletion of own account in 7 days, confirmed by the emailed code (no password since 2026-11-17) | User | `DeleteAccountRequestViewModel` (body: `code`) | `bool` |
| PATCH | `profiles/recover` | Cancel the current user's pending account deletion (the session is enough) | User | none | `bool` |
| GET | `handles/validate` | Check whether a profile handle is available | User | query: `handle` | `string` |

### ReportsController
`src/Presentation/Api/Controllers/ReportsController.cs` — route `api/v1/reports`, signed-in users (since 2026-11-23; gama-api's `reports` before). See `docs/business/support-and-social.md`, "Content reports".

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| POST | `` | Report a problem with a paper, exam, exam question, tutorial, multimedia file or Q&A; one open report per user and item (`alreadyReported`) | User | `CreateContentReportRequestViewModel` (body: `contentType`, `contentId`, `reportType` 1-20, `message` 25-500) | `long` |

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
| GET | `search` | Places by name for the school finder's area box (since 2026-11-10): countries, states and cities whose name or local name contains `query` (2-100 characters), each with its state and country. Names starting with the text first, then places with schools (most first). Up to `size` (max 20). Cached 1 h | Anonymous | query: `query`, `size` | `IEnumerable<LocationSearchResponseViewModel>` |
| GET | `countries` | List countries (paged) | Anonymous | `LocationsRequestViewModel` (query) | `ListDataSource<LocationsResponseViewModel>` |
| GET | `states/{countryId}` | List states of a country (paged) | Anonymous | route: `countryId` + `LocationsRequestViewModel` (query) | `ListDataSource<LocationsResponseViewModel>` |
| GET | `cities/{stateId}` | List cities of a state (paged) | Anonymous | route: `stateId` + `LocationsRequestViewModel` (query) | `ListDataSource<LocationsResponseViewModel>` |

### McpController — MCP connector (protocol endpoints, 2026-10-08)
`src/Presentation/Mcp/McpController.cs` (the MCP presentation project, added as an application part by `AddGamaMcp`) — MVC controller, class-level `[AllowAnonymous]`, hidden from Swagger, routed at the site root (not `api/v1`). OAuth 2.1 authorization server for MCP clients plus the exam import's signed figure upload link. Answers in OAuth JSON / HTML with real HTTP status codes, **not** the `ApiResponse` envelope. See `authentication.md`, "MCP connector (OAuth)", and `docs/business/exams-and-content.md`, "Exam import through the MCP connector".

| Verb | Route | Purpose | Auth | Request | Response |
|---|---|---|---|---|---|
| GET | `/.well-known/oauth-authorization-server` | RFC 8414 metadata (issuer, endpoints, `S256`, auth methods) | Anonymous | none | `McpAuthorizationServerMetadataDto` |
| POST | `/oauth/register` | RFC 7591 dynamic client registration; `201` | Anonymous | `McpClientRegistrationRequestDto` (JSON) | `McpClientRegistrationResponseDto`, or `400 {error, error_description}` |
| GET | `/oauth/authorize` | Checks the authorization request (PKCE `S256`, registered redirect URI, `resource`) and shows the Gamatrain sign-in page; errors go back to the redirect URI when it is known | Anonymous | `response_type`, `client_id`, `redirect_uri`, `state`, `code_challenge`, `code_challenge_method`, `scope`, `resource` (query) | HTML, or `302` |
| POST | `/oauth/authorize` | The sign-in form: gama-api login (one-time-code step for weak passwords), then `302` to the redirect URI with `code` and `state` | Anonymous | `request`, `identity`, `password`, `code` (form) | `302`, or HTML |
| POST | `/oauth/token` | `authorization_code` grant with PKCE; `Cache-Control: no-store` | Anonymous (client secret by `client_secret_post` or Basic for confidential clients) | `grant_type`, `code`, `redirect_uri`, `client_id`, `client_secret`, `code_verifier`, `resource` (form) | `McpTokenResponseDto`, or `400`/`401 {error, error_description}` |
| POST | `/mcp/figures/{link}` | Uploads a figure image (PNG/JPEG ≤ 5 MB) for the link's owner (to this backend's `ExamImage` container once the MCP's exam routes are `Native`) | Signed upload link (2 h, from `get_figure_upload_link`; carries the caller's gama-api token, protected) | multipart `file` | `{ok, figure, name, size}` (`figure` = gama-api's upload key), or `400 {ok: false, message, code}` |

Also served: `GET /.well-known/oauth-protected-resource/mcp` (RFC 9728 protected resource metadata, by the MCP SDK's `AddMcp` scheme).

**`/mcp`** — the MCP server itself (`ModelContextProtocol.AspNetCore`, stateless Streamable HTTP, `MapGamaMcp` in `Startup.ConfigureCore`), authorization `McpToken` only. Only `POST` is served (stateless: no `GET` event stream, no session to `DELETE`); `GET`/`DELETE /mcp` answer `405` with `Allow: POST` (since 2026-10-09; before, they fell through to the MVC route and redirected to `/swagger`). None of these is in Swagger (`ExcludeFromDescription`, since 2026-11-23; the two `405` handlers used to show there under the assembly's name): `/mcp` is MCP's own protocol for AI clients, not part of the REST API. Tools (`src/Presentation/Mcp/ExamImportTools.cs`, each with readOnly/destructive/openWorld hints, results are JSON text `{"ok": true, ...}` / `{"ok": false, "message", "code", "details"}`; since 2026-10-10 the flow tools end their result with the next step, `ask` (a question for the user: `id`, `question`, `options[{key, label, detail, next, ask}]`, `field{label, next, example}`, `next`) or `next` (the assistant's own work), built in `ExamImportFlow.cs`): `open_exams` (the entry point, "gamatrain exams": sign-in and draft check, then Home; replaced `session_status`), `get_import_guide`, `list_options`, `set_exam_details` (`confirmed=false` checks the details and asks with the details card, a missing or invalid one as a question to pick it from gama-api's list; `confirmed=true` creates the draft exam on gama-api or changes it and returns its `examId`), `find_past_papers` (board, grade, subject, year, session and, optionally, paper type `paperId`; 15 a page, `page`), `search_papers` (the Gamatrain directory: a paper ID opens it, words find titles, 15 a page), `list_recent_papers` (the directory's latest papers, newest session first, 15 a page; until 2026-10-14 `GET tests`, only a teacher's own papers) and `load_paper` (open to every signed-in user since 2026-10-14; staff only before, `code: staffOnly`), `add_figure` (`openai/fileParams: ["file"]`; uploads to gama-api), `get_figure_upload_link`, and, all with the draft's `examId`, `save_questions`, `open_review` (the review: one flagged question at a time, from the save results the assistant passes back), `remove_question`, `show_preview` (MCP Apps widget `ui://gamatrain/exam-preview.html`, resource in `ExamImportPreviewWidget.cs`), `publish_exam` and `discard_draft` (without `confirmed` they ask), then `list_my_exams` (the user's drafts and published exams, 20 a page, and what to do with one) and `sign_out` (revokes this connection's access token). One prompt, `exams` (a slash command in Claude Code: `/mcp__gamatrain__exams`), starts the flow. Nothing is stored in this API (except a signed-out token on the cache's deny-list): each call works on gama-api with the caller's token.

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

### NudgesController
`src/Presentation/Api/Controllers/NudgesController.cs` — not to be confused with the Admin-only `NudgesController` below (NudgeTemplate CRUD). See `docs/business/notifications.md`, "Nudge system"

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| GET | `unsubscribe` | One-click unsubscribe from nudge emails — the link every nudge email carries; the token itself is the credential, never expires | Anonymous | query: `userId`, `token` | `bool` |
| PUT | `subscription` | Toggle the caller's own nudge subscription — the counterpart the unsubscribe link can't offer (opting back in) | User | query: `subscribed` | `bool` |

### ActivitiesController
`src/Presentation/Api/Controllers/ActivitiesController.cs` — route `api/v1/activities` (2026-11-15). Reads and podcast listens of the signed-in user; see `docs/business/exams-and-content.md`, "Reads and listens"

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| POST | (root) | Record a read of a post (`PostRead`) or a listen to its podcast (`PodcastPlay`, `PodcastListen`), once per user, kind, post and day; a repeat is a success that changes nothing. `NotFound` for an unpublished or missing post, or a post without a podcast for the podcast kinds | User | `RecordUserActivityRequestViewModel` (body): `type` (smart enum name), `contentId` (post id) | `bool` |

### WeeklyReportsController
`src/Presentation/Api/Controllers/WeeklyReportsController.cs` — route `api/v1/weeklyreports`. The weekly report email, a plan feature; see `docs/business/notifications.md`, "Weekly report"

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| GET | `settings` | The caller's own setting: `subscribed`, `includedInPlan` (their current plan includes the report), `email`, `lastSentDate` (`emailConfirmed` removed 2026-11-15: the report no longer requires it) | User | none | `WeeklyReportSettingsResponseViewModel` |
| PUT | `settings` | Turn the caller's own weekly report on or off | User | query: `subscribed` | `bool` |
| GET | `unsubscribe` | One-click unsubscribe, the link every report carries; the token is the credential (Data Protection, purpose `WeeklyReportUnsubscribe`), never expires. Stops only the report, not nudges | Anonymous | query: `userId`, `token` | `bool` |

### PastPapersController
`src/Presentation/Api/Controllers/PastPapersController.cs` — per-action `[Permission(policy: null)]` (User). A teacher's own
past papers, answered as gama-api answers a teacher (`data` as gama-api sends it), from gama-api (with the caller's token)
or this backend as `Migration:Routes:PastPapers.Teacher` says. Per user, so `Cache-Control: private,no-store`. See
`docs/business/past-papers.md`, "A teacher's own papers".

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| GET | `mine` | The caller's papers, newest first, in any status (gama-api's `GET tests` for a teacher): `{list, num}` | User (bearer or gama-api legacy JWT) | query: `page`, `perpage` (max 50), `section`, `base`, `course`, `lesson`, `test_type`, `variant`, `edu_year`, `edu_month`, `id`, `status` | `JsonObject` |
| POST | `mine/uploads` | Upload one of a paper's files (gama-api's `POST upload`): `[{"file": {"name": key}}]`; `kind=pdf` marks the question PDF (pages and thumbnail). Up to 50 MB. Follows `Migration:WriteRoutes:PastPapers.Teacher` | User | form: `file`; query: `kind` | `JsonNode` |
| POST | `mine` | Add a paper (gama-api's `POST tests` fields, files by upload key); waits for review; `{id}` | User (teachers) | body: gama-api's fields | `JsonNode` |
| PUT | `mine/{id:long}` | Edit the caller's paper (gama-api's `PUT tests/{id}`): `{id, status, editMode}` plus `ignoredFields`/`message` | User | body: gama-api's fields | `JsonNode` |
| DELETE | `mine/{id:long}/{type?}` | Delete the caller's paper (`all`) or a file (`word`, `answer`, `extra` with the extra file id as `id`) until it is past review | User | route: `id`, `type` | `JsonNode` |
| GET | `mine/{id:long}` | One paper for the edit form (gama-api's `GET tests/{id}?for=edit`): any status for its owner, with `edit` (`mode`, `editableFields`, `canDeleteFiles`, `message`, `reviewNotes`); someone else's unpublished paper is denied; no view counted | User | route: `id` | `JsonObject` |

### PaymentsController
`src/Presentation/Api/Controllers/PaymentsController.cs` — class-level `[Permission(policy: null)]` (User, no anonymous overrides except `RecurringWebhook`, explicitly `[AllowAnonymous]` since it's called by the payment gateway, not a logged-in user). `VerifyPayment` has known hardening needs around concurrent verification and caller authorization — see [`docs/business/payments-and-points.md`](../business/payments-and-points.md) (details kept in an internal, non-public review rather than this repo). `VerifyPayment` also now branches on whether the payment was created for a subscription purchase (see `SubscriptionsController.PurchaseSubscription` below and `docs/business/subscriptions.md`) — same route and response shape either way, only the server-side effect differs.

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| POST | `` | Create a payment (returns gateway redirect URL); a Stripe subscription purchase gets a real recurring checkout instead of a one-time charge (see `docs/business/subscriptions.md`'s "Native recurring billing") | User | `CreatePaymentRequestViewModel` (body) | `CreatePaymentResponseViewModel` |
| POST | `{id:long}/verify` | Verify a payment transaction with the gateway; activates a subscription instead of crediting points when the payment was for one. The payment must be the caller's (anyone else's answers `PaymentNotFound`) and, for Stripe, `transactionId` must be the Checkout Session created for it. A payment the checkout webhook already completed answers success (`true`) without granting again (since 2026-10-18) | User | route: `id` + `VerifyPaymentRequestViewModel` (body) | `bool` |
| POST | `webhooks/{gateway:PaymentGateway}` | Stripe webhook receiver (`invoice.paid`/`customer.subscription.deleted`/`invoice.payment_failed` - the third is visibility-only, see `docs/business/subscriptions.md` "Dunning visibility" - plus, since 2026-10-18, `checkout.session.completed`/`async_payment_succeeded` (completes the checkout's Pending payment, same code as `verify`) and `checkout.session.expired`/`async_payment_failed` (closes it as Failed)) - route is gateway-parameterized so a future gateway (PayPal) needs no new route. Always 200s; raw body/signature verified inside the resolved `IRecurringPaymentGatewayProvider`, not in the action | Anonymous (gateway-called) | route: `gateway` + raw request body | `Void` |

### QuestionsController
`src/Presentation/Api/Controllers/QuestionsController.cs` — class-level `[Permission(policy: null)]` (User, no anonymous overrides)

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| GET | `` | Get random questions (correct-answer indices AES-encrypted using caller's token as key material) | User | `RandomQuestionsRequestViewModel` (query) | `IEnumerable<RandomQuestionResponseViewModel>` |

### ReferralController
`src/Presentation/Api/Controllers/ReferralController.cs` — class-level `[Permission(policy: null)]` (User, no anonymous overrides)

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| POST | `generate` | Generate the current user's referral ID (fails if they already have one). Older endpoint; a code links new users only once its owner joined the invite program | User | none | `ReferralReponseViewModel` |
| GET | `` | Teacher invite program status: `isTeacher`, `joined`, locked `percent`, `joinPercent`, the offer (`offerPercent`, `offerExpirationDate`, `offerOpen`), `referralCode`, `invitedCount`, `subscribedCount`, `earnedUsd`. See `docs/business/referrals.md` | User | none | `ReferralStatusResponseViewModel` |
| POST | `join` | Teachers only: join the invite program, locking the percent for life (the offer's while open, else the current setting) and creating the invite code. Repeat calls change nothing | User | none | `ReferralStatusResponseViewModel` |
| GET | `invited` | The caller's invited users, newest first: short name only (`Sara K.`), join date, subscribed now, payment count, earned USD | User | `InvitedUsersRequestViewModel` (query: `pagingDto`) | `ListDataSource<InvitedUserResponseViewModel>` |

### SchoolsController
`src/Presentation/Api/Controllers/SchoolsController.cs` — class-level `[Permission(policy: null)]`; several read actions override with `[AllowAnonymous]`

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| GET | `` | Search/list schools (country/state/city/name/rating/image/board/tuition, `SchoolTypes` (any of), `Tags` (facility tag ids, all of), geo radius `Location` or map box `Bounds`; paged). Sortable `PagingDto.SortFilter` columns include `lastModifyDate` (last activity: edit, approved image or comment, else creation), `hasImage` (schools with a photo first, `Desc`), `distance`, `rating` and `reviewCount`; the site's default is `hasImage` then `lastModifyDate`, both served by an index (since 2026-11-09). An unknown column is ignored. Items carry `schoolType`, `reviewCount` and `tagIds` (since 2026-11-10) | Anonymous | `SchoolInfoRequestViewModel` (query) | `ListDataSource<SchoolInfoResponseViewModel>` (via `OkWithFilter`, echoes filters) |
| GET | `facets` | For the list's filters: total, with photo / rating / tuition, counts per school type and facility tag, and with `IncludeRegions=true` counts per country, state or city one level below the location filter. Cached 5 min | Anonymous | `SchoolFacetsRequestViewModel` (query) | `SchoolFacetsResponseViewModel` |
| GET | `map/{zoom:int}/{x:int}/{y:int}` | One web-map tile of schools for the list's filters (area filters ignored): each school when the tile has up to 60, else counts per cell of a 4×4 grid; `total` is exact. Cached 10 min. Invalid tile → `succeeded:false` | Anonymous | route: tile + `SchoolFilterRequestViewModel` (query) | `SchoolMapTileResponseViewModel` |
| GET | `compare` | Up to 4 schools side by side: details plus parents' rating per topic. 0 or more than 4 ids → `succeeded:false` | Anonymous | query: `ids` (repeat) | `IEnumerable<SchoolCompareResponseViewModel>` |
| GET | `{id:long}` | Get full school detail; enqueues background view-count increment | Anonymous | route: `id` | `SchoolResponseViewModel` |
| GET | `member-roles` | The roles a user can join a school in (name, value, title, `canShowName`) | Anonymous | — | `IEnumerable<SchoolMemberRoleViewModel>` |
| GET | `memberships` | The caller's schools, one item per school and role | User | — | `IEnumerable<UserSchoolResponseViewModel>` |
| GET | `{schoolId:long}/members` | Named teachers/staff (at most 50) and member counts per role | Anonymous | route: `schoolId` | `SchoolMembersResponseViewModel` |
| POST | `{schoolId:long}/members` | Join the school in a role (again: updates `showName`); also adds the matching account role | User | route: `schoolId` + `JoinSchoolRequestViewModel` (body: `role`, `showName`) | `bool` |
| DELETE | `{schoolId:long}/members/{role:SchoolMemberRole}` | Leave the school in that role (account role kept) | User | route params | `bool` |
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
| POST | `{schoolId:long}/contributions` | Propose edits to an existing school; values equal to the school's or empty are dropped, and one that changes nothing is refused (`noChange`, since 2026-11-21) | User | route: `schoolId` + `ManageSchoolContributionRequestViewModel` (body) | `ManageSchoolContributionResponseViewModel` |
| PUT | `{schoolId:long}/contributions/{contributionId:long}` | Update an existing school-edit contribution | User | route params + `ManageSchoolContributionRequestViewModel` (body) | `ManageSchoolContributionResponseViewModel` |
| POST | `contributions` | Propose a brand-new school (with optional default image/comment) | User | `ManageNewSchoolContributionRequestViewModel` (multipart form) | `ManageSchoolContributionResponseViewModel` |
| GET | `{schoolId:long}/issues` | List caller's "school issue" contributions for a school (paged) | User | route: `schoolId` + `SchoolIssuesContributionListRequestViewModel` (query) | `ListDataSource<SchoolIssuesContributionResponseViewModel>` |
| POST | `{schoolId:long}/issues` | Report an issue about a school | User | route: `schoolId` + `ManageSchoolIssuesContributionRequestViewModel` (body) | `ManageSchoolIssuesContributionResponseViewModel` |

### SearchController
`src/Presentation/Api/Controllers/SearchController.cs` — class-level `[Permission(policy: null)]` + `[AllowAnonymous]` + `[EnableCors(PublicCorsPolicy)]`. A cached proxy in front of gama-api's public reads (search since 2026-11-01, the subject directory list and paper data since 2026-11-02): gama-api itself can't take a cache, and these answers are the same for every visitor. Each call is forwarded **anonymously** (no token or cookie is read) with an allow-listed, sorted query string, its `data` returned as gama-api sends it. A success is cached in Redis for 5 minutes (`SearchService`) and sent with `Cache-Control: public,max-age=60,s-maxage=300` so Cloudflare caches it too; a failure is never cached (`no-store`). The CORS policy allows any origin without credentials, so one cached copy is valid for every site — callers must send these requests without credentials (the frontend uses `credentials: 'omit'`).

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| GET | `` | gama-api `search` (the site's search page). Once `Migration:Routes:PastPapers.Website` is not `Legacy`, a paper search (`type=test`) is the routed paper list (`num`, `list`) with gama-api's `typesstats` as `types_stats` and `papers` set to that total (no `pageInfo`, which the website doesn't read), and another type's `types_stats.papers` is the routed count; the same for a tutorials search (`type=tutorial`/`dars`) and the `tutorials` count once `Migration:Routes:Tutorials.Website` is not `Legacy`, and for an exams search (`type=azmoon`/`exam`/`exams`) and the `exams`/`azmoon` counts once `Migration:Routes:Exams.Website` is not `Legacy` (filters `page`, `perpage`, `title`, `section`, `base`, `lesson`, `topic`; `noTypesStats` leaves the counts out), see `docs/business/tutorials.md`; see `docs/business/past-papers.md` | Anonymous | query: `page`, `perpage`, `noTypesStats`, `title`, `type`, `is_paper`, `section`, `base`, `lesson`, `topic`, `test_type`, `variant`, `exam_type`, `content_type`, `edu_year`, `edu_month` | `JsonObject` (gama-api's `data`) |
| GET | `typesstats` | gama-api `search/typesstats`: result counts per content type. Its `papers` (and `test`) count follows the past papers' route, and its `tutorials` (and `dars`) count the tutorials' route, like the search above | Anonymous | same query as above | `JsonObject` |
| GET | `related` | A page's "related" rows (since 2026-11-16; the website called gama-api directly before). For a paper, exam or tutorial page (`source` `test`/`exam`/`tutorial`) each row comes from its own source on its route (since 2026-11-23); `files`/`questions`/`blogs` are always empty. Other pages' rows are gama-api's. See `docs/business/exams-and-content.md`, "Related content" | Anonymous | query: `source`, `id`, `request` | `JsonObject` (`tests`, `papers`, `files`, `questions`, `exams`, `tutorials`, `blogs`) |
| GET | `homestats` | The home page's counts for a board and grade in one answer: `paper`, `studyMaterials` (the routed paper list's totals), `exam`, `tutorial` from gama-api, plus `teacher` and `school` from this app. `teacher` counts the same profiles the teacher directory lists (`TeacherDirectorySpecification`: public, `Role.Teacher`, named; since 2026-11-07, before that every `Public` profile); `school` counts schools not soft-deleted. A count that couldn't be fetched is missing | Anonymous | query: `section`, `base` | `JsonObject` |
| GET | `types` | gama-api `types/list` for the lists this backend copies (`section`, `base`, `course`, `lesson`, `topic`, `test_type`, `exam_type`, `test_extra_file`, `variant`; any other is refused), cached for everyone; from the copy once `Migration:Routes:Types.Website` is not `Legacy`. See `docs/architecture/legacy-migration.md`, "Types" | Anonymous | query: `type`, `section_id`, `base_id`, `course_id`, `lesson_id`, `is_paper`, `is_school` | `JsonArray` (gama-api's items) |
| GET | `papers` | gama-api `tests/search`: the subject directory's paper list and its other resources (uncached it took 1.4-2.2s). With `directory`, `is_paper=true` and `facet=years`, natively only the years with their paper counts (`{num, years: [{edu_year, num}]}`), for loading the directory one year at a time | Anonymous | query: `page`, `perpage`, `lesson`, `is_paper`, `directory`, `facet`, `edu_year`, `edu_month`, `test_type`, `variant` | `JsonObject` (`list`, `num`) |
| GET | `papers/{id:long}` | gama-api `tests/{id}`: one past paper's public data for its page. A missing or private paper answers `succeeded: false` (not cached), which the page turns into a 404. Note: gama-api counts a view when `tests/{id}` is fetched, so only cache misses count views from this path | Anonymous | route: `id` | `JsonObject` (the paper) |
| GET | `examTests/{identity}` | gama-api `examTests/{code}`: a confirmed question's page (by code, or id), cached; `full=true` adds the topic's first tutorial. From the copy once `Migration:Routes:Exams.Website` is not `Legacy` (since 2026-11-17, `docs/business/exams-migration.md`) | Anonymous | route: `identity`; query: `full` | `JsonObject` |
| GET | `examTests/random` | gama-api `examTests/random`: a random confirmed question's `code` for practice (topic, else subject); **never cached** (`no-store`); follows the same route | Anonymous | query: `lesson`, `topic` | `JsonObject` (`code`, `testImgAnswers`, `paywall`) |
| GET | `tutorials/{id:long}` | gama-api `tutorials/{id}`: one published tutorial for its page (since 2026-11-17), cached like a paper's page (only cache misses count a view; a missing or unpublished tutorial answers `succeeded: false`, not cached). From the copy once `Migration:Routes:Tutorials.Website` is not `Legacy`: the content's image links point at this backend's storage and the HTML is sanitized; see `docs/business/tutorials.md` | Anonymous | route: `id` | `JsonObject` (the tutorial) |
| GET | `tutorials/lessonTree/{subjectId:long}` | gama-api `tutorials/lessonTree/{id}`: a subject's seasons and chapters, each with up to 20 published tutorials (since 2026-11-17), cached; follows the same route | Anonymous | route: `subjectId` | `JsonObject` (`id`, `title`, `list`) |

### SubjectsController
`src/Presentation/Api/Controllers/SubjectsController.cs` — class-level `[Permission(policy: null)]` + `[AllowAnonymous]` (whole controller anonymous)

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| GET | `` | List subjects, optionally filtered by grade (paged) | Anonymous | `SubjectsRequestViewModel` (query) | `ListDataSource<SubjectsResponseViewModel>` |

### SubscriptionsController
`src/Presentation/Api/Controllers/SubscriptionsController.cs` — class-level `[Permission(policy: null)]` (User, no anonymous overrides). See `docs/business/subscriptions.md` for the purchase → verify → activate lifecycle and quota model.

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| GET | `plans` | List all active subscription plans, each with a `Prices[]` list (one resolved price per `BillingInterval` the plan offers, global default USD) and its feature/quota list | User | none | `IEnumerable<ActiveSubscriptionPlanResponseViewModel>` |
| POST | `plans/{id:long}/purchase` | Start a subscription purchase: resolves price server-side by plan + `BillingInterval` (never a client-supplied amount), creates a `Pending` `UserSubscription` + `Payment`, returns the gateway checkout URL. If the caller already has an `Active` subscription, delegates to the same logic as `subscriptions/me/switch` instead of starting a second one (since 2026-08-16 - fixed 2026-08-15 as an outright rejection first, then changed to delegate; see `docs/business/subscriptions.md`); an immediate upgrade this way requires two calls - `Confirm: false` (or omitted) returns a preview (`requiresConfirmation`, `previewAmount`, `previewCurrency`), resubmitting identically with `Confirm: true` applies it and charges `previewAmount` | User | route: `id` + `PurchaseSubscriptionRequestViewModel` (body: `Gateway`, `BillingInterval`, `Confirm`) | `PurchaseSubscriptionResponseViewModel` (adds `switched`, `requiresConfirmation`, `previewAmount`, `previewCurrency`, and since 2026-10-15 `paymentProcessing` - the upgrade's payment is still being processed by the bank, not declined; the plan switches by itself once it confirms - and `paymentActionUrl` - the bank wants the customer to confirm the payment (3-D Secure / OTP), send them to this Stripe payment page - alongside `userSubscriptionId`/`paymentId`/`url`) |
| POST | `checkout-offers/details` | The checkout recovery offer behind an emailed link (since 2026-10-19, see `docs/business/notifications.md`, "Checkout recovery"): plan, interval, discount, regular and discounted first-payment price, expiry. POST so the token stays out of URLs and access logs. `succeeded: false` with `CheckoutRecoveryOfferNotFound` for a bad token or another user's offer, `CheckoutRecoveryOfferExpired`, or `CheckoutRecoveryOfferNotAvailable` once the caller has an Active subscription or any paid first subscription payment | User | `CheckoutRecoveryOfferRequestViewModel` (body: `Token`) | `CheckoutRecoveryOfferResponseViewModel` (`subscriptionPlanId`, `planTitle`, `billingInterval`, `discountPercent`, `price`, `discountedPrice`, `currency`, `expirationDate`) |
| POST | `checkout-offers/redeem` | Same checks as `checkout-offers/details`, then opens a new Stripe checkout for the offer's plan and interval with a first-payment-only coupon already applied; redirect to `url`. Each call opens a fresh checkout | User | `CheckoutRecoveryOfferRequestViewModel` (body: `Token`) | `PurchaseSubscriptionResponseViewModel` (`userSubscriptionId`, `paymentId`, `url`) |
| GET | `me` | Get the current user's active subscription, including per-feature quota (`limit`/`used`/`remaining`), `autoRenews`, `cancelAtPeriodEnd`, and `pendingSwitchPlanId`/`pendingSwitchPlanTitle`/`pendingSwitchBillingInterval` (all null unless a downgrade is pending; effective at `expirationDate`) | User | none | `UserSubscriptionResponseViewModel` |
| GET | `me/history` | List the caller's own past subscriptions (`Status` Expired or Cancelled only — `Pending`/`Active` excluded, paged, newest first). No `userId`/`externalSubscriptionId`/`gateway` (admin-only, see `admin/subscriptions/users` below) | User | `UserSubscriptionHistoryRequestViewModel` (query, paging only) | `ListDataSource<UserSubscriptionHistoryResponseViewModel>` |
| GET | `me/usage` | The caller's own quota usage log (since 2026-10-27): what used their subscription allowance and when, paged, always newest first (a client sort/search is ignored). Same log as `admin/subscriptions/usage`, always scoped to the caller. A negative `amount` is a refund (the download failed after the charge). `identifierId` is the content item (paper, multimedia, test or exam id, by `featureCode`) | User | `UserSubscriptionUsageRequestViewModel` (query: paging, `featureCode`, `fromDate`, `toDate`) | `ListDataSource<UserSubscriptionUsageEventResponseViewModel>` (`id`, `userSubscriptionId`, `planTitle`, `featureCode`, `featureName`, `amount`, `identifierId`, `creationDate`) |
| GET | `me/usage/summary` | The caller's own per-feature totals for a required date range (since 2026-10-27), refunds netted out of both `totalAmount` and `useCount` | User | `UserSubscriptionUsageSummaryRequestViewModel` (query: `fromDate`, `toDate`, both required) | `IEnumerable<UserSubscriptionUsageSummaryResponseViewModel>` (`featureCode`, `featureName`, `totalAmount`, `useCount`) |
| POST | `me/cancel` | Cancel-at-period-end for the caller's own current active subscription — quota stays usable until `ExpirationDate`. `NotValid`/`SubscriptionNotRecurring` for a one-time/GamaTrain subscription. Idempotent if already requested. Enqueues a confirmation email | User | none | `bool` |
| POST | `me/resume` | Reverse a pending cancel-at-period-end request for the caller's own current active subscription. Idempotent no-op if nothing was pending. Enqueues a confirmation email | User | none | `bool` |
| POST | `me/switch` | Switch the caller's own current active subscription to a different plan and/or (since 2026-08-16) `billingInterval` (Stripe-recurring only). An upgrade (target price beats current - a bigger interval on the same plan always qualifies) applies immediately with a prorated invoice; a downgrade - including a move to a *smaller* interval (fixed 2026-08-19, previously rejected outright as `IntervalDowngradeNotSupported`) - is deferred to the current period's end. An immediate upgrade requires `Confirm: true` to actually apply - `Confirm: false`/omitted returns a no-op preview (`requiresConfirmation`, `previewAmount`, `previewCurrency`) instead of charging (since 2026-08-16). Enqueues a confirmation email once actually applied. Also reachable via `plans/{id}/purchase` above, which is now the recommended single entry point for a generic buy/upgrade UI - `me/switch` stays the better fit for a dedicated manage-subscription screen | User | `SwitchSubscriptionPlanRequestViewModel` (body: `subscriptionPlanId`, optional `billingInterval`, `Confirm`) | `SwitchSubscriptionPlanResponseViewModel` (adds `requiresConfirmation`, `previewAmount`, `previewCurrency`, and since 2026-10-15 `paymentProcessing`/`paymentActionUrl` (see `plans/{id}/purchase`) alongside `success`/`immediate`/`effectiveDate`) |

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
| GET | `{id:long}` | Get a caller-owned ticket's details (owned = same `UserId`, or same *confirmed* email) | User | route: `id` | `TicketResponseViewModel` |
| GET | `{id:long}/replys` | List replies on a caller-owned ticket, oldest first; marks read-by-user | User | route: `id` | `IEnumerable<TicketReplyResponseViewModel>` |
| POST | `{id:long}/replys` | Reply to a caller-owned ticket as the user, optional file attachment; a ticket that isn't the caller's returns `succeeded:false` | User | route: `id` + `ReplyTicketByUserRequestViewModel` (multipart form) | `Void` (no data) |
| POST | `` | Create a new support ticket (captcha); works whether or not caller is authenticated. Signed-in callers' account email is used and the posted `Email` ignored; `Email` is required only for anonymous callers / accounts without an email | Anonymous (usable while authenticated too) | `CreateTicketRequestViewModel` (multipart form) | `ManageTicketResponseViewModel` |
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

---

## Admin controllers (`src/Presentation/Api/Areas/Admin/Controllers/`)

Base route: `api/v{version:apiVersion}/[area]/[controller]` → `api/v1/admin/<controller>`.
Every controller in this area declares class-level `[Common.DataAnnotation.Area(nameof(Admin), "Admin")]`
(or the equivalent `nameof(Role.Admin)` form — same resolved area name) and class-level
`[Permission(Roles = [nameof(Role.Admin)])]`. **No action in any of the 20 Admin controllers
carries `[AllowAnonymous]` or a different role** — the whole area is uniformly Admin-only; the
Auth column is omitted per-row below and stated once per controller instead.

### ApplicationSettingsController — Admin-only
`src/Presentation/Api/Areas/Admin/Controllers/ApplicationSettingsController.cs` — route `api/v1/admin/applicationsettings`

| Verb | Route | Purpose | Request model | Response model |
|---|---|---|---|---|
| GET | `` | Get current application settings (points, templates, timezone, page size; since 2026-10-19 also `checkoutRecoveryDiscountPercent` (1-90, default 20) and `checkoutRecoveryOfferDays` (1-30, default 7) for the checkout recovery email, returned with their defaults when never set; since 2026-10-21 the teacher invite program's `referralCommissionPercent` (0-100, default 15), `referralOfferValidDays` (0-365, default 30, 0 = never expires), `referralOfferEmailSubject` and `referralOfferEmailTemplate` (must contain `[PERCENT]` and `[JOIN_LINK]`), see `docs/business/referrals.md`; since 2026-10-26 `examAnalysisPoints` (0-1,000,000, default 100), the price of one AI exam analysis) | none | `ApplicationSettingsViewModel` |
| PUT | `` | Update application settings | `ApplicationSettingsViewModel` (body) | `bool` |

### PastPapersController — Admin-only
`src/Presentation/Api/Areas/Admin/Controllers/PastPapersController.cs` — route `api/v1/admin/pastpapers`, `[DelegablePermission]`.
Every paper from this backend's copy, in gama-api's shape. Reads work at any time; every change answers `notNative` until
`Migration:WriteRoutes:PastPapers.Teacher` is `Native`. See `docs/business/past-papers.md`, "Admin review".

| Verb | Route | Purpose | Request model | Response model |
|---|---|---|---|---|
| GET | `` | Every paper not deleted, newest first (`order=idasc`/`iddesc` sorts by id, `downloadsasc`/`downloadsdesc` by downloads, since 2026-11-23); each row has `downloads`; `{list, num}`, each with `user_` and `owner_name` | query: `page`, `perpage`, `status`, `section`, `base`, `course`, `lesson`, `test_type`, `edu_year`, `edu_month`, `variant`, `id`, `code`, `is_paper` (1/0), `uid`, `refScore`, `title`, `order` (since 2026-11-23: `code`, `is_paper`, `order`) | `JsonObject` |
| GET | `{id:long}` | One paper in any status, full `edit`, `reviewNote`, `ownerUserId` | route: `id` | `JsonObject` |
| GET | `{id:long}/files/{type}` | A short signed link to a file for the review (since 2026-11-21): `type` `pdf`, `word`, `answer` or `extra` (with `extraId` from `files.extra`); any status, deleted papers too; nothing charged or counted, independent of the Downloads route | route: `id`, `type`; query: `extraId` | `JsonObject` (`url`) |
| POST | `uploads` | Upload a file (as the teacher's upload) | form: `file`; query: `kind` | `JsonNode` |
| POST | `` | Add a paper for a teacher (`uid`, optional `price_multiplier`) | body: gama-api's fields | `JsonNode` |
| PUT | `{id:long}` | Edit any field; the status stays | body: gama-api's fields | `JsonNode` |
| DELETE | `{id:long}/{type?}` | Delete (hide) a paper or a file, any status | route | `JsonNode` |
| PUT | `{id:long}/review` | Set the status (0 waiting, 1 confirmed, 2 rejected, 3 needs changes, 4 deactivated); notes required unless confirmed | `ReviewPastPaperRequestViewModel` | `JsonObject` (`id`, `status`) |
| PUT | `{id:long}/score` | Referee score 0-5 | `PastPaperScoreRequestViewModel` | `JsonObject` (`id`, `ref_score`) |

### TutorialsController — Admin-only
`src/Presentation/Api/Areas/Admin/Controllers/TutorialsController.cs` — route `api/v1/admin/tutorials`, `[DelegablePermission]`
(since 2026-11-17). The tutorials of this backend's copy. Reads work at any time; every change answers `notNative` until
`Migration:WriteRoutes:Tutorials` is `Native`. Images in the content are uploaded through `EditorImages`. See
`docs/business/tutorials.md`, "Admin".

| Verb | Route | Purpose | Request model | Response model |
|---|---|---|---|---|
| GET | `` | Tutorials in any status, latest change first, with type titles, topics, keywords and owner | query: `status` (name or value), `boardId`, `gradeId`, `subjectId`, `topicId`, `title` (title, description, keywords, or the id), `ownerUserId`, `page`, `pageSize` (max 100) | `TutorialListResponseViewModel` (`total`, `items`) |
| GET | `{id:long}` | One tutorial for the edit form: its HTML (gama-api's image links on this backend's storage, sanitized) and description | route: `id` | `TutorialResponseViewModel` |
| POST | `` | Add a tutorial owned by the caller (`Pending` unless a status is sent); answers its id | `TutorialRequestViewModel` | `long` |
| PUT | `{id:long}` | Edit it; the status stays unless sent, keywords stay unless sent | `TutorialRequestViewModel` | `bool` |
| PUT | `{id:long}/status` | Set the status (`Pending`, `Confirmed` = on the website, `Rejected`, `NeedsChanges`, `Deactivated`) | `TutorialStatusRequestViewModel` | `bool` |
| DELETE | `{id:long}` | Delete for good (its image rows too; the files stay) | route | `bool` |
| POST | `{id:long}/keywords` | The AI's keywords (saved on the tutorial) and learning clusters (shown only), with this backend's AI provider | route | `TutorialKeywordsResponseViewModel` |

### ExamsController — Admin-only
`src/Presentation/Api/Areas/Admin/Controllers/ExamsController.cs` — route `api/v1/admin/exams`, `[DelegablePermission]` (since
2026-11-18). The exams of this backend's copy; every change answers `notNative` until `Migration:WriteRoutes:Exams` is
`Native`. See `docs/business/exams-migration.md`, "Admin".

| Verb | Route | Purpose | Request model | Response model |
|---|---|---|---|---|
| GET | `` | Exams, latest change first | query: `status`, `boardId`, `gradeId`, `subjectId`, `examTypeId`, `ownerUserId`, `text`, `combination` (1 albums, 2 own, 3 mixed), `open` (can be taken now: no dates or within them), `year`, `includeDeleted`, `page`, `pageSize` (max 100) (since 2026-11-23: `combination`, `open`, `year`) | `ExamAdminListDto` |
| GET | `{id:long}` | One exam with its questions in order | route | `ExamAdminDto` |
| PUT | `{id:long}` | Edit its settings | `ExamEditRequestViewModel` | `bool` |
| PUT | `{id:long}/status` | Set its status (gama-api's codes) | `ExamStatusRequestViewModel` | `bool` |
| GET | `reviews` | Exam reviews, latest change first (since 2026-11-23); reviewer name, no email | query: `status` (Confirmed = shown, Rejected = hidden), `examId`, `withComment`, `page`, `pageSize` (≤ 100) | `ExamReviewAdminListDto` |
| PUT | `reviews/{id:long}/status` | Hide a review (`confirm` false, optional note for its author) or show it again | `ExamReviewModerationRequestViewModel` | `bool` |
| PUT | `{id:long}/questions` | Replace its questions, in this order | `ExamQuestionsRequestViewModel` | `bool` |
| DELETE | `{id:long}` | Hide it (results stay) | route | `bool` |

### ExamQuestionsController — Admin-only
`src/Presentation/Api/Areas/Admin/Controllers/ExamQuestionsController.cs` — route `api/v1/admin/examquestions`,
`[DelegablePermission]` (since 2026-11-18). The question bank; same `notNative` rule.

| Verb | Route | Purpose | Request model | Response model |
|---|---|---|---|---|
| GET | `` | Questions, latest change first | query: `status`, `boardId`, `subjectId`, `topicId`, `type`, `ownerUserId`, `text`, `page`, `pageSize` (max 100) | `ExamQuestionAdminListDto` |
| GET | `{id:long}` | One question for the edit form | route | `ExamQuestionAdminDto` |
| POST | `` | Add a question owned by the caller; answers its id | `ExamQuestionRequestViewModel` | `long` |
| PUT | `{id:long}` | Edit it | `ExamQuestionRequestViewModel` | `bool` |
| PUT | `{id:long}/status` | Set its status (0 waiting, 1 confirmed, 2 rejected) | `ExamStatusRequestViewModel` | `bool` |
| DELETE | `{id:long}` | Delete one no exam uses (`usedByExam` otherwise) | route | `bool` |

### ExamParticipationsController — Admin-only
`src/Presentation/Api/Areas/Admin/Controllers/ExamParticipationsController.cs` — route `api/v1/admin/exam-participations`
(2026-11-18). **Not** delegable: it shows participants' names and emails. Read-only, from `ExamAttempts`.

| Verb | Route | Purpose | Request model | Response model |
|---|---|---|---|---|
| GET | `` | Attempts, newest first, with exam title, participant (name, email; null for a gama-api user not linked yet), status (0 started, 1 finished), result and seconds taken; plus `total`, `finished` and distinct `participants` of all matching attempts | query: `examId`, `userId`, `status`, `from`, `to` (start date, `to` exclusive), `text` (name, user name or email), `page`, `pageSize` (max 100) | `ExamParticipationListDto` |

### TopicMatchesController — Admin-only
`src/Presentation/Api/Areas/Admin/Controllers/TopicMatchesController.cs` — route `api/v1/admin/topicmatches`,
`[DelegablePermission]` (since 2026-11-17). The review of the suggested matches between GAMA's topics and other boards'.
Reads work at any time; every change answers `notNative` until `Migration:WriteRoutes:Types` and `Types.TopicMatches` are
`Native`. See `docs/business/tutorials.md`, "Topic matching".

| Verb | Route | Purpose | Request model | Response model |
|---|---|---|---|---|
| GET | `` | Matches by GAMA topic, most confident first, with both sides' board/grade/subject/topic and the GAMA topic's first tutorial | query: `topicId` (either side), `gamaSubjectId`, `externalSubjectId`, `status` (`Pending`/`Approved`, name or value), `page`, `pageSize` (max 200) | `TopicMatchListResponseViewModel` |
| POST | `` | Add a match decided by a person (waiting); `duplicateItem` if suggested already | `TopicMatchAddRequestViewModel` | `long` |
| POST | `{id:long}/approve` | Approve: link the two topics | route | `bool` |
| DELETE | `{id:long}/approve` | Take back: remove the link, the match waits again | route | `bool` |
| DELETE | `{id:long}` | Remove a suggestion (refused while approved) | route | `bool` |
| POST | `suggest/{topicId:int}` | The AI's matches for a topic (90+ confidence saved as waiting); answers the topic's matches and the subjects that failed | route | `TopicMatchSuggestResponseViewModel` |

### TypesController — Admin-only
`src/Presentation/Api/Areas/Admin/Controllers/TypesController.cs` — route `api/v1/admin/types`, `[DelegablePermission]`.
The lookup lists in this backend's copy (boards 1, grades 2, subjects 4, courses 5, classifications 6, topics 11, extra
file types 20, papers 22, variants 29). Changes answer `notNative` until `Migration:WriteRoutes:Types` is `Native`. See
`docs/architecture/legacy-migration.md`, "Types".

| Verb | Route | Purpose | Request model | Response model |
|---|---|---|---|---|
| GET | `` | A list's items in list order | query: `kind`, `parent`, `title`, `includeDeleted`, `page`, `pageSize` | `TaxonomyItemListDto` |
| GET | `{id:int}` | One item | route: `id` | `TaxonomyItemDto` |
| POST | `` | Add an item (next id) | `TypeItemRequestViewModel` | `int` |
| PUT | `{id:int}` | Edit an item (same list) | `TypeItemRequestViewModel` | `bool` |
| DELETE | `{id:int}` | Hide an item | route: `id` | `bool` |

### PostsController — Admin-only
`src/Presentation/Api/Areas/Admin/Controllers/PostsController.cs` — route `api/v1/admin/posts`

| Verb | Route | Purpose | Request model | Response model |
|---|---|---|---|---|
| GET | `` | List posts of every status (filter by status/title/date/email/username/featured; since 2026-11-10 `hasPodcast` — `false` = posts without a podcast file, `true` = with one) | `AdminPostsRequestViewModel` (query) | `ListDataSource<ManagedPostsResponseViewModel>` |
| GET | `{postId:long}` | Get one post for editing/review, any status, incl. every localized value | route: `postId` | `PostEditResponseViewModel` |
| PATCH | `{postId:long}/confirm` | Approve a post in `Review` or `Rejected` (points are awarded only from `Review`; emails the author) | route: `postId` | `bool` |
| PATCH | `{postId:long}/reject` | Reject a post in `Review` or `Confirmed` (unpublishes it) with a comment (max 300 chars) | `RejectRequestViewModel` (body) + route `postId` | `bool` |
| PUT | `{postId:long}` | Edit any post (admin); does not change its status | `UpdatePostRequestViewModel` (multipart form) + route `postId` | `ManagePostResponseViewModel` |
| DELETE | `{postId:long}` | Delete a post | route: `postId` | `bool` |
| GET | `comments` | List post comments of every status (filter by status/date/commenter) | `AdminPostCommentsRequestViewModel` (query) | `ListDataSource<ManagedPostCommentsResponseViewModel>` |
| PATCH | `comments/{commentId:long}/confirm` | Approve a comment in `Review` or `Rejected` | route: `commentId` | `bool` |
| PATCH | `comments/{commentId:long}/reject` | Reject a comment in `Review` or `Confirmed` with a comment (max 300 chars) | `RejectRequestViewModel` (body) + route `commentId` | `bool` |
| GET | `site-maps` | List sitemap entries for posts | `SiteMapListRequestViewModel` (query) | `ListDataSource<SiteMapListResponseViewModel>` |
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

### CommissionsController — Admin-only
`src/Presentation/Api/Areas/Admin/Controllers/CommissionsController.cs` — route `api/v1/admin/commissions`

| Verb | Route | Purpose | Request model | Response model |
|---|---|---|---|---|
| GET | `` | Report of accrued `ContentOwnerCommission` rows across all owners (filterable by `startDate`/`endDate`, `reason`, and optionally `ownerUserId` to see one owner). Every accrual, paid out or not | `AdminContentOwnerCommissionsListRequestViewModel` (query) | `ListDataSource<ContentOwnerCommissionListResponseViewModel>` |
| GET | `payouts` | Payout requests across owners (filter `status`, `userId`), newest first, with who approved/paid/rejected each and when | `AdminCommissionPayoutsListRequestViewModel` (query) | `ListDataSource<CommissionPayoutResponseViewModel>` |
| PATCH | `payouts/{payoutId:long}/approve` | Pending → Approved. Needs the admin's authenticator code; not on own request. A `StripeConnect` payout is sent through Stripe here and becomes Paid (Stripe's error returned, payout back to Pending, if it fails) | `PayoutDecisionRequestViewModel` (body: `twoFactorCode`) | `bool` |
| PATCH | `payouts/{payoutId:long}/reject` | Pending (or Approved Manual) → Rejected, releases the amount | `RejectPayoutRequestViewModel` (body: `twoFactorCode`, `reason`) | `bool` |
| PATCH | `payouts/{payoutId:long}/paid` | Approved → Paid. Manual: confirms the money was transferred (`transferReference` required). Stripe: finishes an approval that stopped half-way | `MarkPayoutPaidRequestViewModel` (body: `twoFactorCode`, `transferReference`) | `bool` |

### ContentLocalizationsController — Admin-only
`src/Presentation/Api/Areas/Admin/Controllers/ContentLocalizationsController.cs` — route `api/v1/admin/contentlocalizations`

| Verb | Route | Purpose | Request model | Response model |
|---|---|---|---|---|
| GET | `` | List content localization entries | `ContentLocalizationsRequestViewModel` (query) | `ListDataSource<ContentLocalizationsResponseViewModel>` |
| GET | `{id:long}` | Get content localization detail | route: `id` | `ContentLocalizationResponseViewModel` |
| POST | `` | Create a content localization entry | `ManageContentLocalizationRequestViewModel` (body) | `ManageContentLocalizationResponseViewModel` |
| PUT | `{id:long}` | Update a content localization entry | `ManageContentLocalizationRequestViewModel` (body) + route `id` | `ManageContentLocalizationResponseViewModel` |
| DELETE | `{id:long}` | Delete a content localization entry | route: `id` | `bool` |

### DashboardController — Admin-only (except `summary`)
`src/Presentation/Api/Areas/Admin/Controllers/DashboardController.cs` — route `api/v1/admin/dashboard` (2026-10-22). Read-only. See `docs/business/admin-dashboard.md`.

| Verb | Route | Purpose | Request model | Response model |
|---|---|---|---|---|
| GET | `summary` | "Needs attention" queues (unread contact us, school contributions/images/issues/removals/comments, posts, post comments, payouts; since 2026-11-23 also papers, exams, questions and tutorials waiting) with count, new in the last 7 days and oldest date. Cached for a minute; also feeds the admin menu badges. **Any signed-in user** since 2026-11-23: all queues for an admin, only their granted lists' queues for delegated staff, none otherwise (`docs/business/admin-dashboard.md`, "Delegated staff") | none | `AdminDashboardSummaryResponseViewModel` |
| GET | `overview` | Sign-ups (teachers/students), paid payments by kind in USD, subscriptions (active, cancelling, new, expired), exam participation (attempts, finished, participants, top 10 exams) and a daily series, for whole UTC days `startDate`..`endDate` (default last 30 days, max 366) | `DashboardRangeRequestViewModel` (query) | `AdminDashboardOverviewResponseViewModel` |
| GET | `referrals` | Teacher invite program for the range: funnel, all-time top 20 teachers, latest 50 invited sign-ups with a possible self-invite flag | `DashboardRangeRequestViewModel` (query) | `AdminReferralReportResponseViewModel` |

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
| GET | `` | List users (filter by referral, name, email, referral ID, roles; e.g. `?Roles=Teacher`, repeat `Roles` to match any of several). Filters combine (AND); before 2026-11-07 only the last one given was applied, and until 2026-11-09 the `Roles` filter was ignored entirely (the flags-enum query binder never matched). Each row carries the user's `roles` as a name array (e.g. `["Teacher"]`) | `UserListRequestViewModel` (query) | `ListDataSource<UserListResponseViewModel>` |
| GET | `{userId:long}` | Get user detail | route: `userId` | `UserResponseViewModel` |
| POST | `` | Create a user | `CreateUserRequestViewModel` (body) | `Void` (no data) |
| PUT | `{userId:long}` | Edit a user | `EditUserRequestViewModel` (body) + route `userId` | `Void` (no data) |
| DELETE | `{userId:long}` | Delete a user. Needs the calling admin's current authenticator code (step-up, since 2026-11-09) | `TwoFactorCodeRequestViewModel` (body: `code`) + route `userId` | `bool` |
| PATCH | `{userId:long}/toggle` | Enable/disable a user. Needs the calling admin's current authenticator code (step-up, since 2026-11-09) | `TwoFactorCodeRequestViewModel` (body: `code`) + route `userId` | `Void` (no data) |
| GET | `{userId:long}/token` | View a user's API access token | route: `userId` | `GetTokenResponseViewModel` |
| POST | `{userId:long}/token` | Generate a new API access token for a user | route: `userId` | `GenerateTokenResponseViewModel` |
| DELETE | `{userId:long}/token` | Revoke a user's API access token | route: `userId` | `bool` |
| PUT | `{userId:long}/reset-password` | Reset a user's password | `ResetPasswordRequestViewModel` (body) + route `userId` | `Void` (no data) |
| GET | `{userId:long}/permissions` | View a user's permission tree (only delegable endpoints, see `docs/business/identity-and-access.md`, "Delegated admin permissions"), roles, and system claims | route: `userId` | `UserPermissionsResponseViewModel` |
| PUT | `{userId:long}/permissions` | Replace a user's endpoint permissions, roles, and system claims (an omitted `permissions` list clears them). Needs the calling admin's current authenticator code in `twoFactorCode` (step-up, since 2026-11-09) | `ManageUserPermissionsRequestViewModel` (body) + route `userId` | `Void` (no data) |

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
`src/Presentation/Api/Areas/Admin/Controllers/LocationsController.cs` — route `api/v1/admin/locations`. Also `[DelegablePermission]` (since 2026-11-10): each endpoint can be granted to a non-admin one by one (see `docs/business/identity-and-access.md`, "Delegated admin permissions").

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

### NudgesController — Admin-only
`src/Presentation/Api/Areas/Admin/Controllers/NudgesController.cs` — route `api/v1/admin/nudges`. See `docs/business/notifications.md`, "Nudge system"

| Verb | Route | Purpose | Request model | Response model |
|---|---|---|---|---|
| GET | `templates` | List nudge templates (one per `NudgeType`) | `NudgeTemplatesRequestViewModel` (query) | `ListDataSource<NudgeTemplateResponseViewModel>` |
| GET | `templates/{id:int}` | Get a nudge template by id | route: `id` | `NudgeTemplateResponseViewModel` |
| POST | `templates` | Create a nudge template | `ManageNudgeTemplateRequestViewModel` (body) | `ManageNudgeTemplateResponseViewModel` |
| PUT | `templates/{id:int}` | Update a nudge template (partial — only non-null fields overwrite) | `ManageNudgeTemplateRequestViewModel` (body) + route `id` | `ManageNudgeTemplateResponseViewModel` |
| DELETE | `templates/{id:int}` | Remove a nudge template | route: `id` | `bool` |

### ActivitiesController — Admin-only
`src/Presentation/Api/Areas/Admin/Controllers/ActivitiesController.cs` — route `api/v1/admin/activities` (2026-11-15). Reads and podcast listens; Admin role only, not delegable (personal reading history). See `docs/business/exams-and-content.md`, "Reads and listens"

| Verb | Route | Purpose | Auth | Request model | Response model |
|---|---|---|---|---|---|
| GET | `posts` | The posts with the most readers in the window, with listeners and full listens (different users each) | Admin | `TopPostsRequestViewModel` (query): `Days` (1-365, default 30), `Size` (1-100, default 20) | `IEnumerable<PostActivityResponseViewModel>`: `postId`, `title`, `readers`, `listeners`, `fullListens` |
| GET | `users/{userId}` | One user's reads and listens in the window, newest first, at most 200 | Admin | query: `days` (1-365, default 30) | `IEnumerable<UserActivityItemResponseViewModel>`: `day`, `type`, `postId`, `title` |

### WeeklyReportsController — Admin-only
`src/Presentation/Api/Areas/Admin/Controllers/WeeklyReportsController.cs` — route `api/v1/admin/weeklyreports`. Sends nothing.

| Verb | Route | Purpose | Request model | Response model |
|---|---|---|---|---|
| GET | `status` | Whether this server may send it (`enabledOnServer`, the `WeeklyReport:Enabled` config) and whether an admin paused it (`paused`) | none | `WeeklyReportStatusResponseViewModel` |
| PUT | `status` | Pause or resume the weekly report for everyone, from the next run; no deploy | query: `paused` | `bool` |
| GET | `preview` | One user's email for a week (default last week) exactly as the job would send it, whether it would be sent and why not (`notSentReason`) | `WeeklyReportPreviewRequestViewModel` (query: `userId`, `weekOf`) | `WeeklyReportPreviewResponseViewModel` |

### PaymentsController — Admin-only
`src/Presentation/Api/Areas/Admin/Controllers/PaymentsController.cs` — route `api/v1/admin/payments`

| Verb | Route | Purpose | Request model | Response model |
|---|---|---|---|---|
| GET | `` | List payments (filterable by date range, user, gateway, status, and — since 2026-09-16 — `Kind`: `PointsTopUp`/`NewSubscription`/`Renewal`/`PlanSwitch`, null for pre-existing rows that can't be reliably backfilled, see `docs/business/payments-and-points.md`) — lists the local `Payment` table only; an immediate plan-switch's Stripe proration charge is included here too since 2026-08-16 (previously silently missing — see `docs/business/subscriptions.md`, "Immediate plan-switch charges weren't recorded as Payments") | `PaymentsListRequestViewModel` (query) | `ListDataSource<PaymentsListResponseViewModel>` |
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

### ReportsController — Admin (delegable)
`src/Presentation/Api/Areas/Admin/Controllers/ReportsController.cs` — route `api/v1/admin/reports`, Admin role or a delegated grant (since 2026-11-23). Reporter's name, never their email.

| Verb | Route | Purpose | Request model | Response model |
|---|---|---|---|---|
| GET | `` | Reports, newest first, with the item's title when it lives here | query: `status` (Open, Resolved, Dismissed), `contentType`, `contentId`, `page`, `pageSize` (≤ 100) | `ContentReportListDto` |
| PUT | `{id:long}/status` | Resolve, dismiss or reopen, with an optional note | `ContentReportStatusRequestViewModel` | `bool` |

### SchoolsController — Admin-only
`src/Presentation/Api/Areas/Admin/Controllers/SchoolsController.cs` — route `api/v1/admin/schools`

| Verb | Route | Purpose | Request model | Response model |
|---|---|---|---|---|
| GET | `` | List schools | `SchoolsRequestViewModel` (query) | `ListDataSource<SchoolsResponseViewModel>` |
| GET | `{id:long}` | Get a school by id | route: `id` | `SchoolResponseViewModel` |
| GET | `members` | School memberships (what users said they are at a school), newest first, with the member and school; the count per role, distinct users and schools of all matching ones. Read-only; names and emails, so this Admin-only controller stays non-delegable (since 2026-11-23) | query: `role` (Teacher, Staff, Student, Parent, Alumni or its number), `schoolId`, `userId`, `text`, `page`, `pageSize` (≤ 100) | `SchoolMemberAdminListDto` |
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
| GET | `contributions/{contributionId:long}` | Get a school-edit or new-school contribution (old vs. new values) for review; since 2026-11-10 also the photo (`imageUri`, `isDefaultImage`) and the comment with ratings (`comment`) submitted with it, both null when none was sent | route: `contributionId` | `SchoolContributionReviewViewModel` |
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
`src/Presentation/Api/Areas/Admin/Controllers/SubscriptionsController.cs` — route `api/v1/admin/subscriptions`. Plans no longer carry `price`/`currency`/`point`/`billingInterval` directly (see `docs/business/subscriptions.md`) — those moved to the `prices` and `plans/{id}/features` endpoints below; a plan is the product, each price row is one billing-interval SKU.

| Verb | Route | Purpose | Request model | Response model |
|---|---|---|---|---|
| GET | `plans` | List subscription plans (with prices + features) | `SubscriptionPlansRequestViewModel` (query) | `ListDataSource<SubscriptionPlanResponseViewModel>` |
| GET | `plans/{id:long}` | Get a subscription plan by id | route: `id` | `SubscriptionPlanResponseViewModel` |
| POST | `plans` | Create a subscription plan | `ManageSubscriptionPlanRequestViewModel` (body) | `ManageSubscriptionPlanResponseViewModel` |
| PUT | `plans/{id:long}` | Update a subscription plan | `ManageSubscriptionPlanRequestViewModel` (body) + route `id` | `ManageSubscriptionPlanResponseViewModel` |
| DELETE | `plans/{id:long}` | Remove a subscription plan; fails if any `UserSubscription` ever referenced it | route: `id` | `bool` |
| GET | `features/codes` | List the fixed set of `Feature.Code` values a call site actually enforces (`FeatureCodes` constants) — for populating a `Code` dropdown, not a DB-backed list | none | `IEnumerable<string>` |
| GET | `features` | List the feature catalog | `FeaturesRequestViewModel` (query) | `ListDataSource<FeatureResponseViewModel>` |
| POST | `features` | Create a feature; `Code` (if given) must be one of `features/codes`, else `NotValid` | `ManageFeatureRequestViewModel` (body) | `ManageFeatureResponseViewModel` |
| PUT | `features/{id:int}` | Update a feature; `Code` (if given) must be one of `features/codes`, else `NotValid` | `ManageFeatureRequestViewModel` (body) + route `id` | `ManageFeatureResponseViewModel` |
| DELETE | `features/{id:int}` | Remove a feature | route: `id` | `bool` |
| GET | `plans/{id:long}/features` | Get a plan's feature groups — one entry per quota bucket (`features: [...]` has one code when unpooled, several when pooled), `limit`/`description` stated once per group, `description` already resolved server-side | route: `id` | `IEnumerable<PlanFeatureGroupViewModel>` |
| PUT | `plans/{id:long}/features` | Replace a plan's entire feature/limit set, expressed as `featureGroups: [{ featureIds: [...], limit, description }]` — a group of 2+ feature ids pools them onto one shared limit and requires `description` (a pool has no single feature to describe it); a group of one is a normal independent feature and ignores `description` | route: `id` + `SetPlanFeaturesRequestViewModel` (body) | `bool` |
| POST | `plans/{id:long}/sync-features` | Give the plan's current features to every Active subscriber on it who is missing them (added 2026-10-30; quotas are snapshotted at activation, so a feature added to a plan later never reached existing subscribers). Additive only: a feature added to a group the subscriber already has a bucket for joins that bucket (shares its remaining balance, bucket description refreshed); a feature from a new group gets a new bucket at the plan's limit for the subscriber's own interval, `used` 0. Existing `limit`/`used` never change, nothing is removed, and running it again is a no-op. **Requires the admin's authenticator code** (step-up 2FA, same as payout decisions); refused without 2FA enabled or with a wrong/used code, before anything is read. See `docs/business/subscriptions.md`, "Syncing plan features to existing subscribers" | route: `id` + `SyncPlanFeaturesRequestViewModel` (body: `twoFactorCode`) | `SyncPlanFeaturesResponseViewModel`: `subscriptionCount` (subscriptions that gained a feature), `featureCount` (features attached in total), `quotaCount` (new buckets created) |
| GET | `prices` | List plan prices (paged) | `SubscriptionPlanPricesRequestViewModel` (query) | `ListDataSource<SubscriptionPlanPriceResponseViewModel>` |
| POST | `prices` | Create a plan price (`countryCode: null` = the plan's global default for that `billingInterval`) | `ManageSubscriptionPlanPriceRequestViewModel` (body: includes required `billingInterval`) | `ManageSubscriptionPlanPriceResponseViewModel` |
| PUT | `prices/{id:long}` | Update a plan price | `ManageSubscriptionPlanPriceRequestViewModel` (body) + route `id` | `ManageSubscriptionPlanPriceResponseViewModel` |
| DELETE | `prices/{id:long}` | Remove a plan price | route: `id` | `bool` |
| GET | `gateway-mappings` | List gateway Product/Price mappings (paged) | `GatewayMappingsRequestViewModel` (query) | `ListDataSource<GatewayMappingResponseViewModel>` |
| POST | `gateway-mappings` | Create a gateway mapping — written now, not yet read by anything until native recurring billing ships (see `docs/business/subscriptions.md`) | `ManageGatewayMappingRequestViewModel` (body) | `ManageGatewayMappingResponseViewModel` |
| PUT | `gateway-mappings/{id:long}` | Update a gateway mapping | `ManageGatewayMappingRequestViewModel` (body) + route `id` | `ManageGatewayMappingResponseViewModel` |
| DELETE | `gateway-mappings/{id:long}` | Remove a gateway mapping | route: `id` | `bool` |
| GET | `users` | List any user's subscription(s) for support cases, filterable by `userId`/`status` and searchable with `search` (the subscriber's email, first name, last name or full name contains the term; since 2026-10-15) (paged); `featureGroups` (quota status) is never set here — see `users/{id}` below | `AdminUserSubscriptionsRequestViewModel` (query) | `ListDataSource<AdminUserSubscriptionResponseViewModel>` |
| GET | `users/{id:long}` | Get a user subscription's detail, including `externalSubscriptionId`/`gateway` (never exposed on `subscriptions/me`), `pendingSwitchPlanId`/`pendingSwitchPlanTitle`/`pendingSwitchBillingInterval`, and (since 2026-08-17) `featureGroups` — current live quota status (`limit`/`used`/`remaining` per feature group, `remaining: null` when `limit` is null/unlimited) | route: `id` | `AdminUserSubscriptionResponseViewModel` |
| POST | `users/grant` | Comped grant for a support case — creates and activates a subscription immediately, bypassing payment (`pricePaid` recorded as 0). **No check for an existing active subscription** — granting a plan to a user who already has one active creates a second, simultaneously-active row (harmless for quota-stacking purposes, but worth knowing before using this on a user you're not sure about) | `GrantUserSubscriptionRequestViewModel` (body) | `GrantUserSubscriptionResponseViewModel` |
| POST | `users/{id:long}/revoke` | Immediate revocation for a support case (unlike the user-facing cancel-at-period-end flow) — terminates the gateway-side subscription first if recurring, then cancels locally right away. Only ever touches the one subscription matched by `id`; a user's other subscriptions are unaffected | route: `id` | `bool` |
| POST | `users/{id:long}/extend` | Push `ExpirationDate` forward by `days` for a support case — local record only, never re-bills, never touches the gateway, and never resets quota `Used` the way a real renewal would | route: `id` + `ExtendUserSubscriptionRequestViewModel` (body) | `bool` |
| POST | `users/{id:long}/resync` | Source-of-truth counterpart to `extend`: reads the gateway's own live status/current period end and, if it confirms this subscription is still active, syncs `ExpirationDate` directly to it and resets quota — the same self-healing check `ExpireOverdueSubscriptions` (below) does automatically once past its own grace period, available here immediately for a support case, no body/day-count guess needed. `NotValid`/`SubscriptionNotRecurring` for a one-time/GamaTrain subscription (nothing to ask a gateway about — use `extend` instead). `synced: false` (not an error) whenever the gateway reports anything other than active with a future period end, or (since 2026-10-05) its latest invoice is not yet paid — `gatewayStatus` carries the raw reason (fixed 2026-09-05) — see `docs/business/subscriptions.md`, "Reconciling against the gateway before expiring" | route: `id` | `ResyncSubscriptionResponseViewModel` (`synced`, `newExpirationDate`, `gatewayStatus`) |

**Sharp edge — `{id}` on every `users/{id:long}/...` route above is the `UserSubscriptionId`, not the `UserId`**, despite the `users/` path segment. Easy to get wrong: the admin UI's own subscription list can show the same `UserId` on two different rows (one user, two subscriptions) with no visibly distinct subscription id in the table, tempting a caller into passing the `UserId` instead. Use `GET users/{id}`'s own `id` field (not its `userId` field) to confirm you have the right value before calling `revoke`/`extend`.
| GET | `usage` | Raw consumption event log (`SubscriptionQuotaConsumptionLog`), filterable by `userId`/`featureCode`/`identifierId`/`fromDate`/`toDate` (paged) — `identifierId` is which content item (e.g. a pastpaper id), same filter `admin/transactions` already offers on `Transaction`. Since 2026-11-07 a refund row carries `refundOfLogId` (the charge it reverses) and a refunded charge carries `refundLogId` | `SubscriptionUsageHistoryRequestViewModel` (query) | `ListDataSource<SubscriptionUsageEventResponseViewModel>` |
| POST | `usage/{id:long}/refund` | Refund one charge for a support case (since 2026-11-07): credits its quota back to the subscription, capped at the bucket's current `used`, through the same refund as a failed download. **Step-up 2FA.** A charge is refunded once; `NotValid` for a refund row, an already-refunded charge, an inactive subscription or nothing left to credit. See `docs/business/subscriptions.md`, "Admin refund of a usage event" | route: `id` (the usage log id) + `RefundUsageEventRequestViewModel` (body: `twoFactorCode`) | `RefundUsageEventResponseViewModel` (`refundedAmount`) |
| GET | `usage/aggregate` | Per-feature usage totals (`totalAmount`/`eventCount`/`refundCount`/`distinctUserCount`) for a required date range — scoped to one user when `userId` is supplied, a global usage dashboard otherwise | `SubscriptionUsageAggregateRequestViewModel` (query) | `IEnumerable<SubscriptionUsageAggregateResponseViewModel>` |

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
| GET | `` | List tickets, latest activity first (read or not; a client `SortFilter` is ignored). Optional filters: `unread` (`true`: ticket or any reply unread by admin; `false`: ticket and all replies read; omitted: all), `search` (contains on subject/name/email; a ticket number like `123`/`Ticket-123` also matches the id), `email` (exact sender), `startDate`/`endDate` (inclusive, on `LastActivityDate`) | `AdminTicketsRequestViewModel` (query) | `ListDataSource<TicketsResponseViewModel>` |
| POST | `` | Send a new ticket/email to a user, optional attachment | `SendTicketRequestViewModel` (multipart form) | `ManageTicketResponseViewModel` |
| GET | `{id:long}` | Get ticket details; marks it read-by-admin (never back to unread) | route: `id` | `TicketResponseViewModel` |
| GET | `{id:long}/replys` | Get all replies for a ticket, oldest first; marks replies read-by-admin | route: `id` | `IEnumerable<TicketReplyResponseViewModel>` |
| POST | `{id:long}/replys` | Reply to a ticket, optional attachment | `ReplyTicketByAdminRequestViewModel` (multipart form) + route `id` | `Void` (no data) |
| PATCH | `{id:long}/toggle` | Toggle a ticket's "read by admin" flag | route: `id` | `bool` |
| POST | `{id:long}/ai-replies` | Suggest a reply with AI: `mode` `Generate` writes one from the ticket, `Polish` improves `draft`. Grounded in the support handbook; the customer name and email are not sent to the model, the server adds the `Hi {first name},` greeting. Generate also gets plain-text findings from the customer's account (subscriptions, payments, quota), and the response's `notes` lists them with ids for the admin only; returns HTML for the editor, sends nothing, doesn't change read flags. See `docs/business/support-and-social.md`, "AI reply assistant" | `TicketAiReplyRequestViewModel` (body) + route `id` | `TicketAiReplyResponseViewModel` (`reply`, `notes`) |
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

### TwoFactorController — Admin-only
`src/Presentation/Api/Areas/Admin/Controllers/TwoFactorController.cs` — route `api/v1/admin/twofactor`. The calling
admin's own authenticator-app (TOTP) second factor, required by sensitive admin actions; see
`docs/business/identity-and-access.md`, "Authenticator two-factor for admin actions".

| Verb | Route | Purpose | Request model | Response model |
|---|---|---|---|---|
| GET | `` | Whether the caller has 2FA enabled | — | `TwoFactorStatusResponseViewModel` |
| POST | `setup/email-code` | Email a 6-digit setup code to the caller's confirmed address; returns the masked address. Refused without a confirmed email or while 2FA is on | — | `string` |
| POST | `setup` | With the emailed code, generate a new key; returns `sharedKey` + `authenticatorUri` (show as a QR code). Refused while 2FA is on | `TwoFactorSetupRequestViewModel` (body: `emailCode`) | `AuthenticatorSetupResponseViewModel` |
| POST | `enable` | Turn 2FA on with a code from the app | `TwoFactorCodeRequestViewModel` (body) | `bool` |
| POST | `disable` | Turn 2FA off; needs a current code | `TwoFactorCodeRequestViewModel` (body) | `bool` |
| POST | `users/{userId:long}/reset` | Lost-device recovery: turn another user's 2FA off; needs the caller's own code | `TwoFactorCodeRequestViewModel` (body) + route `userId` | `bool` |

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
| GET | `summary` | Daily payments summary over a date range, filterable by user, gateway, status, currency, and — since 2026-09-16 — `Kind`. Two independent per-day pivots of the same rows: paid/pending/failed amounts+counts (by `Status`, as before), and — new — `NewSubscription`/`Renewal`/`PlanSwitch`/`PointsTopUp` amounts+counts (by `Kind`, null-`Kind` rows excluded — see `docs/business/payments-and-points.md`) | `PaymentsSummaryRequestViewModel` (query) | `IEnumerable<PaymentsSummaryResponseViewModel>` |

---

## Notable attribute/behavior mismatches found while cataloging

A few actions have a `[Produces(...)]` attribute that doesn't match what the method body actually
returns. These are source-level inconsistencies (not doc errors) — the table entries above already
report the *actual* runtime response type, with the mismatch called out:

- `ExamsController.Export` — declares generic `IActionResult`, actually branches between `ApiResponse<Void>` and a raw file stream.
- `FilesController.GetFile` — declares `ApiResponse<string>`, can also short-circuit to an HTTP redirect.
- `SchoolsController` (public) `RequestRemoveSchoolImage`-style action — declares `bool`, returns `RemoveSchoolImageContributionResponseViewModel`.
- `LocationsController` (Admin) `UpdateState` — declares `Void`, returns `ManageLocationResponseViewModel`.
- `PaymentsController` (Admin) `Export` — declares `ApiResponse<string>`, success path returns a raw `FileContentResult` (xlsx download).

Also flagged for review (not mismatches, but noteworthy deviations from the codebase's own
conventions): `HomeController` has no `[Route]`/`[ApiVersion]`/auth attribute at all;
and `ExamsController` omits `[ApiVersion("1.0")]`. (`VotingPowersController`, whose anonymous bulk-import `POST`
relied only on an in-body signature, was removed on 2026-11-23.)

`GamesController` was the first controller in the solution to carry two `[ApiVersion]`
attributes (`spends` needed a v1-compatible bare-`bool` response alongside a richer v2
response for the new subscription-quota upsell — see `docs/business/subscriptions.md`).
Every other action on it has no `[MapToApiVersion]` and so is served under both versions
unchanged.
