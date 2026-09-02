# Database Schema Catalog

Every entity in `src/Domain/Entity/` (and `src/Domain/Entity/Identity/`), grouped by business domain. Table names, columns, and FK actions below were cross-checked against the live dev SQL Server instance (`INFORMATION_SCHEMA` + `sys.foreign_keys`), not just the C# — see `docs/database/overview.md` for the shared EF conventions (`VersionableEntity`, `IDeletable`, `IContentLocalizeable`, smart enums) referenced throughout.

Legend: **PK** = primary key. FK `NoAction`/`Cascade` reflects the actual `ON DELETE` behavior in the live database.

---

## Identity & Access

| Entity (file) | Table | Purpose | Key fields | FK relationships |
|---|---|---|---|---|
| `ApplicationUser` (`Identity/ApplicationUser.cs`) | `ApplicationUsers` | Core user record; extends ASP.NET Core `IdentityUser<long>` with app-specific profile fields | `Id` (PK), `UserName`, `Email`, `PasswordHash`, `Enabled`, `FirstName`/`LastName`, `Avatar`/`AvatarId`, `Gender`, `Board`/`Grade`/`Group` (plain ints, not FKs), `CurrentBalance` (points ledger cache), `WalletId`, `ProfileVisibility`, `Handle`, `ReferralId`, `LastLoginDate`, `OrphanDate` | `CityId` → `Locations` (NoAction), `SchoolId` → `Schools` (NoAction). Seeded row `Id=1` (`admin`/`@Admin123`, `ApplicationUser.cs:229-236`). Audited (`[Audit]`). |
| `ApplicationRole` (`Identity/ApplicationRole.cs`) | `ApplicationRoles` | Role definitions | `Id` (PK), `Name`/`NormalizedName` | Seeded: `Admin`(1), `Teacher`(2), `Student`(3), `Advisor`(4), `Finance`(5) — `ApplicationRole.cs:56-63`, matching the `Role` smart enum (`src/Domain/Enumeration/Role.cs`) |
| `ApplicationUserRole` | `ApplicationUserRoles` | User↔Role join | PK `(UserId, RoleId)` | `UserId` → `ApplicationUsers` (NoAction), `RoleId` → `ApplicationRoles` (NoAction). Seeded: user 1 → role 1 (Admin). |
| `ApplicationRoleClaim` | `ApplicationRoleClaims` | Claims attached to a role | `Id` (PK), `ClaimType`, `ClaimValue`, `RoleId` | `RoleId` → `ApplicationRoles` (NoAction) |
| `ApplicationUserClaim` | `ApplicationUserClaims` | Claims attached to a user | `Id` (PK), `ClaimType`, `ClaimValue`, `UserId` | `UserId` → `ApplicationUsers` (NoAction). Audited. |
| `ApplicationUserLogin` | `ApplicationUserLogins` | External login providers (OAuth) linked to a user | PK `(LoginProvider, ProviderKey)`, `UserId` | `UserId` → `ApplicationUsers` (NoAction) |
| `ApplicationUserToken` | `ApplicationUserTokens` | Identity token store (used by the custom opaque-token auth scheme) | PK `(UserId, LoginProvider, Name)`, `Value` | `UserId` → `ApplicationUsers` (implicit) |
| `ApplicationUserPasskey` | `ApplicationUserPasskeys` | WebAuthn/passkey credentials | `CredentialId` (PK, `varbinary(1024)`), `Data` (JSON via `OwnsOne(...).ToJson()`), `UserId` | `UserId` → `ApplicationUsers` (**Cascade** — the only Identity FK that cascades) |
| `LoginHistory` (`LoginHistory.cs`) | `LoginHistories` | Append-only login audit log | `Id` (PK), `UserId`, `CreationDate`, `IpAddress`, `UserAgent` | `UserId` → `ApplicationUsers` (NoAction) |

---

## Schools & Locations

| Entity (file) | Table | Purpose | Key fields | FK relationships |
|---|---|---|---|---|
| `School` (`School.cs`) | `Schools` | A school directory listing; the app's central aggregate | `Id` (PK), `Name`/`LocalName`, `SchoolType` (smart enum), `CountryId`/`StateId`/`CityId`, `Coordinates` (`geography` `Point`), `RankScore` (computed, see below; internal only, not exposed via API), `CountryRank`/`StateRank`/`CityRank` (computed), `IsDeleted` (soft delete), `ViewCount`, `Tuition`, `DefaultImageId` | `CountryId`/`StateId`/`CityId` → `Locations` (NoAction each); `DefaultImageId` → `SchoolImages` (NoAction); `CreationUserId`/`LastModifyUserId` → `ApplicationUsers`. Has `HasQueryFilter(t => !t.IsDeleted)` — the **only** soft-deletable entity (`School.cs:128`). |
| `SchoolComment` (`SchoolComment.cs`) | `SchoolComments` | User review/rating of a school (7 rating dimensions + free text) | `Id` (PK), `SchoolId`, `Comment`, `ClassesQualityRate`/`EducationRate`/`ITTrainingRate`/`SafetyAndHappinessRate`/`BehaviorRate`/`TuitionRatioRate`/`FacilitiesRate`/`ArtisticActivitiesRate` (each `float`), `AverageRate`, `LikeCount`/`DislikeCount` | `SchoolId` → `Schools` (NoAction). Unique `(CreationUserId, SchoolId)` — one comment per user per school. |
| `SchoolImage` (`SchoolImage.cs`) | `SchoolImages` | Photos attached to a school | `Id` (PK), `SchoolId`, `FileId`, `FileType` (smart enum), `IsDefault`, `TagId`, `ContributionId` | `SchoolId` → `Schools` (NoAction), `TagId` → `Tags` (NoAction), `ContributionId` → `Contributions` (NoAction) |
| `SchoolTag` (`SchoolTag.cs`) | `SchoolTags` | School↔Tag join (amenities/features) | `Id` (PK), `SchoolId`, `TagId` | `SchoolId` → `Schools` (**Cascade**), `TagId` → `Tags` (**Cascade**). Unique `(SchoolId, TagId)`. |
| `SchoolBoard` (`SchoolBoard.cs`) | `SchoolBoards` | School↔Board join (which curricula/boards a school teaches) | `Id` (PK), `SchoolId`, `BoardId` | `SchoolId` → `Schools` (**Cascade**), `BoardId` → `Boards` (**Cascade**). Unique `(SchoolId, BoardId)`. |
| `Board` (`Board.cs`) | `Boards` | Curriculum board/system (e.g. national curricula) | `Id` (PK), `Code` (unique), `Title`, `Description` | `IContentLocalizeable` (translatable `Title`/`Description` via `ContentLocalizations`) |
| `Grade` (`Grade.cs`) | `Grades` | Grade/class level within a `Board` | `Id` (PK), `Title`, `BoardId` | `BoardId` → `Boards` (**Cascade**). Many-to-many with `Subject` via `SubjectGrades`. `IContentLocalizeable`. |
| `Location` (`Location.cs`) | `Locations` | Hierarchical geo entity (country/state/city/…) | `Id` (PK), `Title`/`LocalTitle`, `Code` (unique), `LocationType` (smart enum), `ParentId` (self-FK), `Coordinates` (`geography` `Point`) | `ParentId` → `Locations` (self, NoAction). Bulk-seeded, see `migrations.md` → `ImportLocations`. `IContentLocalizeable`. |
| `Experience` (`Experience.cs`) | `Experiences` | A user's tenure/experience at a school (e.g. "taught at X from…to…") | `Id` (PK), `UserId`, `StartDate`/`EndDate`, `SchoolId`, `Description` | `UserId` → `ApplicationUsers` (NoAction), `SchoolId` → `Schools` (**Cascade**) |

---

## Content / Blog

| Entity (file) | Table | Purpose | Key fields | FK relationships |
|---|---|---|---|---|
| `Post` (`Post.cs`) | `Posts` | Blog article | `Id` (PK), `Slug` (unique), `Title`, `Summary`, `Body`, `ImageId`, `PodcastId`, `LikeCount`/`DislikeCount`, `PublishDate`, `VisibilityType` (smart enum), `ViewCount` | `CreationUserId`/`LastModifyUserId` → `ApplicationUsers`. `IContentLocalizeable`. |
| `PostComment` (`PostComment.cs`) | `PostComments` | Comment on a post | `Id` (PK), `PostId`, `Comment`, `LikeCount`/`DislikeCount` | `PostId` → `Posts` (NoAction). Unique `(CreationUserId, PostId)` — one comment per user per post. |
| `PostTag` (`PostTag.cs`) | `PostTags` | Post↔Tag join | `Id` (PK), `PostId`, `TagId` | `PostId` → `Posts` (**Cascade**), `TagId` → `Tags` (**Cascade**). Unique `(PostId, TagId)`. |
| `Tag` (`Tag.cs`) | `Tags` | Shared tag vocabulary, reused by posts, schools, and school images | `Id` (PK), `Name`, `TagType` (smart enum — discriminates blog tags vs. school-amenity tags vs. image tags, etc.), `Icon` | Unique `(TagType, Name)`. `IContentLocalizeable`. |
| `ContentLocalization` (`ContentLocalization.cs`) | `ContentLocalizations` | Generic translated-field store for any `IContentLocalizeable` entity | `Id` (PK), `ContentType`, `ContentId`, `Name`, `Value`, `LanguageId` | `LanguageId` → `Languages` (**Cascade**). No FK to the owning entity (polymorphic by `ContentType`+`ContentId`, see `overview.md`). Unique `(LanguageId, ContentType, ContentId, Name)`. |
| `Language` (`src/Core/Common/Localization/Language.cs`) | `Languages` | Supported UI/content languages | `Id` (PK), `Name`, `Code` (unique), `IsEnable`, `IsDefault` (unique, filtered to `IsDefault=1`) | — |
| `Video` (`Video.cs`) | *(none)* | YouTube video reference; extends `VersionableEntity` but does **not** implement `IEntity<,>` | `Id`, `YouTubeVideoId`, `Title`, `Description`, `Icon` | **Not currently mapped** — no `Configure(...)`, and no `Videos` table exists in the live database (verified). Effectively dead/half-implemented code; do not assume it's queryable. |

---

## Exams & Curriculum

| Entity (file) | Table | Purpose | Key fields | FK relationships |
|---|---|---|---|---|
| `Subject` (`Subject.cs`) | `Subjects` | A curriculum subject (e.g. Math, Science) | `Id` (PK), `Title`, `Order` | M:N with `Grade` via `SubjectGrades` (`GradeId`, `SubjectId`, both **Cascade**), M:N with `Topic` via `SubjectTopics` (`SubjectId`, `TopicId`, both **Cascade**). `IContentLocalizeable`. |
| `Topic` (`Topic.cs`) | `Topics` | A topic within a subject | `Id` (PK), `Title`, `Order` | M:N with `Subject` via `SubjectTopics`. `IContentLocalizeable`. |
| *(join, no entity class)* | `SubjectGrades` | Subject↔Grade join table, configured inline in `Subject.Configure` (`Subject.cs:36-42`) | `SubjectId`, `GradeId` | Both **Cascade** |
| *(join, no entity class)* | `SubjectTopics` | Subject↔Topic join table, configured inline in `Subject.Configure` (`Subject.cs:44-50`) | `SubjectId`, `TopicId` | Both **Cascade** |
| `Question` (`Question.cs`) | `Questions` | An exam/quiz question | `Id` (PK), `Body`, `Options` (`Collection<QuestionOption>`, stored as **JSON** via `OwnsMany(...).ToJson()`, not a separate table) | `IContentLocalizeable`. `QuestionOption` (`QuestionOption.cs`) is an owned sub-object (`Index`, `Body`, `IsCorrect`), never a standalone table. |
| `ExamSubmission` (`ExamSubmission.cs`) | `ExamSubmissions` | A user's submitted result for an exam | `Id` (PK), `UserId`, `ExamId` (no FK — exams are not modeled as an entity in this codebase), `Valid`/`Invalid`/`NoAnswer` counts, `CreationDate` | `UserId` → `ApplicationUsers` (NoAction). Unique `(UserId, ExamId)`. |
| `TestSubmission` (`TestSubmission.cs`) | `TestSubmissions` | A user's answer to one test question | `Id` (PK), `UserId`, `TestId`, `SubmissionId`, `IsCorrect`, `CreationDate` | `UserId` → `ApplicationUsers` (NoAction). Unique `(UserId, TestId)`. |

---

## Gamification & Points

| Entity (file) | Table | Purpose | Key fields | FK relationships |
|---|---|---|---|---|
| `Transaction` (`Transaction.cs`) | `Transactions` | Append-only points ledger entry (linked-list style) | `Id` (PK), `PreviousTransactionId` (self-FK, unique — enforces a single linear chain per… see note), `UserId`, `IdentifierId` (polymorphic, no FK), `Points`, `CurrentBalance` (running total), `IsDebit`, `TransactionType` (smart enum), `CreationDate` | `UserId` → `ApplicationUsers` (NoAction), `PreviousTransactionId` → `Transactions` (self, NoAction, **unique** index so no two transactions can share a predecessor). This unique constraint is what `TransactionService` relies on to detect/retry concurrent writes (see `ANALYZE.md` §4.4 / §6 B3). |
| `Contribution` (`Contribution.cs`) | `Contributions` | A pending user-submitted edit/addition (e.g. a school image or fact) awaiting moderation, which earns points once approved | `Id` (PK), `CategoryType` (smart enum — what kind of content), `Status` (smart enum — pending/approved/rejected), `Comment`, `Data` (free-form payload), `IdentifierId` (polymorphic, no FK) | `CreationUserId`/`LastModifyUserId` → `ApplicationUsers` |
| `VotingPower` (`VotingPower.cs`) | `VotingPowers` | Snapshot of a wallet's voting weight for a governance proposal (Solana-based) | `Id` (PK), `ProposalId`, `WalletAddress`, `Amount` (precision 36,18), `TokenAccount`, `CreationDate` | No FKs — wallet-address keyed, not user-keyed |

---

## Payments & Subscriptions

See `docs/business/subscriptions.md` for the purchase → verify → activate lifecycle and quota semantics; this table is the schema reference only.

| Entity (file) | Table | Purpose | Key fields | FK relationships |
|---|---|---|---|---|
| `Payment` (`Payment.cs`) | `Payments` | A crypto (or gateway) payment attempt/record. Also the trigger for subscription activation when `UserSubscriptionId` is set | `Id` (PK), `UserId`, `Amount` (precision 36,18), `Currency` (smart enum), `Status` (smart enum), `Gateway` (smart enum), `CreationDate`/`VerifyDate`, `SourceWallet`, `TransactionId`, `UserSubscriptionId` (nullable), `BaseCurrencyAmount`/`ExchangeRate` (nullable, precision 36,18 — USD-reporting amount locked at verify time; only set for USD-pegged currencies today) | `UserId` → `ApplicationUsers` (NoAction). `UserSubscriptionId` → `UserSubscriptions` (NoAction, nullable — **not** the reverse; `UserSubscription` has no `PaymentId` column, avoiding a circular FK). Unique `(TransactionId, Gateway)` — prevents replaying the same on-chain tx under the same gateway. |
| `SubscriptionPlan` (`SubscriptionPlan.cs`) | `SubscriptionPlans` | A purchasable subscription tier (product) — **definition only**, no price and no billing interval (both moved to `SubscriptionPlanPrice`) | `Id` (PK), `Title`, `Polygon` (`geography` — availability region), `IsActive`, `Highlight` | `CreationUserId`/`LastModifyUserId` → `ApplicationUsers` |
| `Feature` (`Feature.cs`) | `Features` | Data-driven catalog of quota-limited actions (e.g. `PastpaperDownload`) | `Id` (PK, int), `Code` (unique), `Name`, `Description`, `IsActive`, `CreationDate` | — (no FK; referenced by `SubscriptionPlanFeature`/`UserSubscriptionQuotaFeature`) |
| `SubscriptionPlanFeature` (`SubscriptionPlanFeature.cs`) | `SubscriptionPlanFeatures` | Per-plan quota limit for one feature **at one billing interval** — Monthly/Annual/etc of the same plan can grant different limits (2026-08-13); still never keyed by price/currency | `Id` (PK), `SubscriptionPlanId`, `FeatureId`, `BillingInterval` (smart enum, byte), `Limit` (int?, `NULL` = unlimited), `FeatureGroupKey` (string?, server-generated GUID; rows sharing a key pool onto one quota — set via `SetPlanFeaturesAsync`'s `FeatureGroups` request shape, never admin-typed; the same key is reused across a group's interval rows), `FeatureGroupDescription` (string?, required whenever a group pools 2+ features, `NULL` otherwise) | `SubscriptionPlanId` → `SubscriptionPlans` (**Cascade**), `FeatureId` → `Features` (NoAction). Unique `(SubscriptionPlanId, FeatureId, BillingInterval)`. |
| `SubscriptionPlanPrice` (`SubscriptionPlanPrice.cs`) | `SubscriptionPlanPrices` | One purchasable SKU: a plan at one region and one billing interval; `CountryCode = NULL` is the global default row for that interval | `Id` (PK), `SubscriptionPlanId`, `CountryCode` (nullable), `Currency` (smart enum), `Price` (precision 36,18), `BillingInterval` (smart enum, carries a `Days` count + `CalculateEndDate` helper) | `SubscriptionPlanId` → `SubscriptionPlans` (**Cascade**). Unique `(SubscriptionPlanId, CountryCode, BillingInterval)` — SQL Server treats `NULL` as a distinct value, so this also enforces at most one default row per plan+interval. |
| `SubscriptionPlanGatewayMapping` (`SubscriptionPlanGatewayMapping.cs`) | `SubscriptionPlanGatewayMappings` | External gateway Product/Price (or Plan) id for one regional, one-interval price; **written by admin now, not yet consumed** — reserved for the native-recurring-billing phase | `Id` (PK), `SubscriptionPlanPriceId`, `Gateway` (smart enum), `ExternalProductId`, `ExternalPlanId` (nullable) | `SubscriptionPlanPriceId` → `SubscriptionPlanPrices` (**Cascade**). Unique `(SubscriptionPlanPriceId, Gateway)`. |
| `UserSubscription` (`UserSubscription.cs`) | `UserSubscriptions` | A user's purchase/enrollment in a plan for one billing period | `Id` (PK), `UserId`, `SubscriptionPlanId`, `Status` (smart enum: Pending/Active/Expired/Cancelled), `CreationDate`, `StartDate`/`ExpirationDate` (nullable, set on activation), `PricePaid` (precision 36,18, snapshot), `Currency` (snapshot), `BillingInterval` (snapshot — which interval of the plan was purchased; used to compute `ExpirationDate` without a join back to the plan/price), `ExternalSubscriptionId` (string?, max 200 — the gateway's own recurring-subscription id, e.g. Stripe `sub_...`; `NULL` for a one-time/GamaTrain subscription or before Stripe activation finishes; doubles as the "is this actually recurring" signal, exposed as `AutoRenews` on `GET subscriptions/me`), `CancelAtPeriodEnd` (bool, default false — set when the user requests cancellation via `POST subscriptions/me/cancel`; doesn't itself change `Status`/`ExpirationDate`, those flip when Stripe's `customer.subscription.deleted` fires at the real period end), `PendingSwitchSubscriptionPlanId` (long?, FK → `SubscriptionPlans`, `NoAction` — set by a deferred downgrade via `POST subscriptions/me/switch`; applied by `RenewSubscriptionAsync` at the next renewal, cleared by a cancellation request), `PendingSwitchPricePaid` (decimal?, precision 36,18 — snapshotted at request time, paired with `PendingSwitchSubscriptionPlanId`) | `UserId` → `ApplicationUsers` (NoAction), `SubscriptionPlanId` → `SubscriptionPlans` (NoAction — blocks deleting a plan that was ever sold), `PendingSwitchSubscriptionPlanId` → `SubscriptionPlans` (NoAction). Indexes `(UserId, Status)`, `(Status, ExpirationDate)`. |
| `UserSubscriptionQuota` (`UserSubscriptionQuota.cs`) | `UserSubscriptionQuotas` | One allowance **bucket** for one `UserSubscription` — covers one feature, or several when pooled (see `UserSubscriptionQuotaFeature` below); `Limit` snapshotted from the group's `SubscriptionPlanFeature.Limit` at activation, matched to the subscription's own `BillingInterval`, `Description` snapshotted already-resolved (the group's `FeatureGroupDescription` when pooled, else the single feature's own `Feature.Description`) | `Id` (PK), `UserSubscriptionId`, `Limit` (int?, snapshotted; `NULL` = unlimited), `Used`, `Description` (string?, snapshotted, always populated) | `UserSubscriptionId` → `UserSubscriptions` (**Cascade**). Remaining (`Limit - Used`, `NULL` if `Limit` is `NULL`) is computed, never stored. |
| `UserSubscriptionQuotaFeature` (`UserSubscriptionQuotaFeature.cs`) | `UserSubscriptionQuotaFeatures` | Join: which feature(s) a `UserSubscriptionQuota` bucket covers — usually one row, more than one when `SubscriptionPlanFeature.FeatureGroupKey` pooled several features at activation | `Id` (PK), `UserSubscriptionQuotaId`, `UserSubscriptionId` (denormalized purely so the unique index below can enforce one bucket per feature per subscription), `FeatureId` | `UserSubscriptionQuotaId` → `UserSubscriptionQuotas` (**Cascade**), `FeatureId` → `Features` (NoAction). Unique `(UserSubscriptionId, FeatureId)`. |
| `SubscriptionQuotaConsumptionLog` (`SubscriptionQuotaConsumptionLog.cs`) | `SubscriptionQuotaConsumptionLogs` | One consumption event, written alongside the guarded `UserSubscriptionQuota.Used` decrement in `ConsumeQuotaAsync` — `Used` is only ever a running counter, this is the event log/audit trail behind it, for per-user history and admin usage reporting | `Id` (PK), `UserId`, `UserSubscriptionId`, `FeatureId` (the specific feature consumed, even for a pooled bucket), `Amount`, `IdentifierId` (nullable, which content item — e.g. a pastpaper id — mirrors `Transaction.IdentifierId`), `CreationDate` | `UserSubscriptionId` → `UserSubscriptions` (**NoAction**), `FeatureId` → `Features` (NoAction). Deliberately **no FK to `UserSubscriptionQuota`** — `CreateQuotasAsync` deletes and re-snapshots a subscription's quota bucket rows on every plan switch/re-activation, so a hard FK there would cascade-delete history out from under a plan switch. No FK on `IdentifierId` either, same reasoning as `Transaction.IdentifierId` — its owning table depends on the feature. Indexes `(UserId, CreationDate)`, `(FeatureId, CreationDate)`. |

---

## Content Delivery / Commissions

See `docs/business/content-delivery.md` for the full download → charge → commission flow; this table is the schema reference only.

| Entity (file) | Table | Purpose | Key fields | FK relationships |
|---|---|---|---|---|
| `ContentOwnerCommission` (`ContentOwnerCommission.cs`) | `ContentOwnerCommissions` | One append-only accrual row: commission owed to a content owner for one paid download of their content. Separate from `Transaction`/points wallet and from subscription quota entirely — an owner's balance is the sum of their rows here, never denormalized elsewhere. No payout/paid-status columns yet (deliberately deferred to a separate phase) | `Id` (PK), `OwnerUserId`, `DownloaderUserId`, `Reason` (smart enum, one member: `ContentDownload` — decoupled from `Source` so a future non-download reason, e.g. a blog-publish bonus, doesn't have to reuse download-shaped columns), `Source` (smart enum, one member: `GamaApiLegacy`), `ContentType` (the broader smart enum shared with `games/spends`, values `PastPaper`/`Test`/`Multimedia`/`Exam` — but this column only ever gets written as `PastPaper`: `POST downloads` uses its own dedicated 3-member `DownloadContentType` enum, and `ContentDeliveryService` hardcodes the mapping to `ContentType.PastPaper` for the one case that ever accrues commission), `ExternalContentId`, `ExternalFileType` (string, e.g. `pdf`), `ExternalExtraId` (nullable), `Points` (snapshot of the source's reported price), `CommissionPercent` (snapshot of `ApplicationSettingsDto.ContentOwnerCommissionPercent` at accrual time), `AmountUsd` (precision 18,4 — locked at accrual time via a fixed 100-points-per-$1 rate), `CreationDate` | `OwnerUserId`/`DownloaderUserId` → `ApplicationUsers` (both NoAction). Indexed on `OwnerUserId`. |

---

## Support / Tickets

| Entity (file) | Table | Purpose | Key fields | FK relationships |
|---|---|---|---|---|
| `Ticket` (`Ticket.cs`) | `Tickets` | A support/contact-us ticket | `Id` (PK), `UserId` (nullable — anonymous contact form submissions allowed), `FullName`, `Email`, `Subject`, `Receivers` (JSON string list), `Body`, `IsReadByAdmin`, `FileId` | `UserId` → `ApplicationUsers` (NoAction, nullable) |
| `TicketReply` (`TicketReply.cs`) | `TicketReplies` | A reply within a ticket thread (admin or user) | `Id` (PK), `TicketId`, `CreationUserId` (nullable), `Body`, `IsRead`, `IsReadByAdmin`, `FileId`, `Receivers` (JSON) | `TicketId` → `Tickets` (**Cascade**) |

---

## Notifications

See `docs/business/notifications.md`, "Nudge system" for the full design.

| Entity (file) | Table | Purpose | Key fields | FK relationships |
|---|---|---|---|---|
| `NudgeTemplate` (`NudgeTemplate.cs`) | `NudgeTemplates` | Admin-editable content for one proactive/scheduled nudge type | `Id` (PK), `NudgeType` (smart enum, unique), `Subject`, `Body` (placeholders `[RECEIVER_NAME]`/`[CTA_URL]`), `CtaLabel`, `CtaUrl`, `IsActive`, `CreationDate` | none |
| `UserNudgeLog` (`UserNudgeLog.cs`) | `UserNudgeLogs` | Tracks nudges already sent to a user — enforces resend cooldown/cap | `Id` (PK), `UserId`, `NudgeType` (smart enum), `LastSentDate`, `SendCount` | `UserId` → `ApplicationUsers` (Cascade). Unique `(UserId, NudgeType)` |

---

## Social / Reactions / Connections

| Entity (file) | Table | Purpose | Key fields | FK relationships |
|---|---|---|---|---|
| `Connection` (`Connection.cs`) | `Connections` | A social follow/friend connection between two users | `Id` (PK), `SourceUserId`, `DestinationUserId`, `Status` (smart enum), `SubscribeToActivityFeed`, `CreationDate` | `SourceUserId`/`DestinationUserId` → `ApplicationUsers` (both NoAction) |
| `Message` (`Message.cs`) | `Messages` | A direct message between two users | `Id` (PK), `SenderId`, `ReceiverId`, `Body`, `IsRead`, `CreationDate` | `SenderId`/`ReceiverId` → `ApplicationUsers` (both NoAction) |
| `Reaction` (`Reaction.cs`) | `Reactions` | A like/dislike on some piece of content | `Id` (PK), `CategoryType` (smart enum — what was reacted to: post, post comment, school comment, …), `IdentifierId` (polymorphic, no FK), `IsLike`, `CreationUserId`/`CreationDate` | Unique `(CategoryType, IdentifierId, CreationUserId)` — one reaction per user per target. `Post.LikeCount`/`PostComment.LikeCount`/`SchoolComment.LikeCount` are denormalized counters kept in sync by Hangfire jobs (`UpdatePostReactionsAsync`, `UpdatePostCommentReactionsAsync`, `UpdateSchoolCommentReactionsAsync` — see `migrations.md`), not by triggers/FKs. |

---

## Audit / System

| Entity (file) | Table | Purpose | Key fields | FK relationships |
|---|---|---|---|---|
| `ApplicationSettings` (`ApplicationSettings.cs`) | `ApplicationSettings` | Generic string key/value app configuration store | `Id` (PK, `string`, no identity generation), `Value` | `CreationUserId`/`LastModifyUserId` → `ApplicationUsers`. Audited (`[Audit]`). |
| `Audit` (`src/Core/Common/DataAccess/Audit/Audit.cs`) | `Audits` | One row per audited `SaveChanges` call | `Id` (`Ulid`, PK), `Date`, `UserId`, `UserName`, `IpAddress`, `UserAgent` | Has many `AuditEntries` |
| `AuditEntry` (`.../Audit/AuditEntry.cs`) | `AuditEntries` | One row per changed entity instance within an audited save | `Id` (`Ulid`, PK), `AuditId`, `EntityType`, `AuditType` (Added/Modified/Deleted), `IdentifierId` | `AuditId` → `Audits` (**Cascade**) |
| `AuditEntryProperty` (`.../Audit/AuditEntryProperty.cs`) | `AuditEntryProperties` | One row per changed property within an audited entity | `Id` (`Ulid`, PK), `AuditEntryId`, `PropertyName`, `OldValue`, `NewValue` | `AuditEntryId` → `AuditEntries` (**Cascade**) |
| `SiteMap` (`SiteMap.cs`) | `SiteMaps` | Precomputed sitemap.xml entries | `Id` (PK), `IdentifierId` (polymorphic, no FK), `ItemType` (smart enum), `Priority`, `ChangeFrequency` (smart enum) | Unique `(ItemType, IdentifierId)`. Regenerated by the `GenerateSiteMap` Hangfire job. |

### Not application tables (present in the DB but out of scope for this catalog)

- **ASP.NET Core Data Protection**: `DataProtectionKeys` — framework-managed, not an EF entity in this project.
- **Hangfire storage** (SQL Server job storage provider): `AggregatedCounter`, `Counter`, `Hash`, `Job`, `JobParameter`, `JobQueue`, `List`, `Schema`, `Server`, `Set`, `State` — owned entirely by the Hangfire library, not by `ApplicationDBContext`'s model.
- `__EFMigrationsHistory` — EF Core's own migration bookkeeping table.
