# Schools Directory

Business logic: `src/Application/Service/SchoolService.cs` (~2076 lines),
contract `src/Application/Interface/ISchoolService.cs`. Generic moderation
plumbing: `src/Application/Service/ContributionService.cs`,
`src/Domain/Entity/Contribution.cs`. Core entity:
`src/Domain/Entity/School.cs`.

## Creating and editing schools

There are two distinct write paths:

- **Admin direct writes** — `ManageSchoolAsync` (`SchoolService.cs:368-548`)
  upserts a `School` row directly, including inline tag/board diffing
  (`:410-468, 493-517`). It's only ever called from the role-gated Admin
  area (`Presentation/Api/Areas/Admin/Controllers/SchoolsController.cs:135,189`,
  `[Permission(Roles = [nameof(Role.Admin)])]` at `:36`).
- **Public submissions via Contribution** — regular users cannot call
  `ManageSchoolAsync`; the public controller instead calls
  `ManageSchoolContributionAsync` (`SchoolService.cs:1493-1578`), which
  wraps the proposed school data as a `Contribution`
  (`CategoryType.School`, `Status.Review`). An admin later reviews it via
  `ConfirmSchoolContributionAsync` (`:1579-1742`), which persists the data
  by calling `ManageSchoolAsync` internally (`:1640`) and can also create a
  default image/comment/localized values in the same confirmation
  (`:1646-1717`), or `RejectSchoolContributionAsync` (`:1744-1784`). Users
  holding the `SystemClaim.AutoConfirmSchoolContribution` claim skip review
  entirely (`:1559-1568`).

`School` (`src/Domain/Entity/School.cs`) has no explicit approval-state
field of its own — the review/approval state lives entirely in
`Contribution`. Schools carry `SchoolType` (enum — `Public`, `Private`,
`Religious`, `FirstNation`, `PrivateNonProfit`, `Government`, `Community`;
`src/Domain/Enumeration/SchoolType.cs:9-27`), location FKs, contact info,
`Coordinates` (a `geography` `Point`), the ranking `Score`, `IsDeleted`
(soft delete), and `CountryRank`/`StateRank`/`CityRank`.

## The Contribution approve/reject workflow (generic)

`Contribution` (`src/Domain/Entity/Contribution.cs:16-48`) is a generic
moderation envelope reused across schools, images, comments, issues, and
blog posts: `CategoryType` (what kind of change), `Status` (state
machine: `Draft` → `Review` → `Confirmed`/`Rejected`, per
`src/Domain/Enumeration/Status.cs:9-21`), a JSON `Data` blob of the proposed
change, and `IdentifierId` linking back to the target record. Confirmation
is admin-only in practice (invoked only from role-gated Admin controllers),
except when the submitting user holds a matching `AutoConfirmXxx`
`SystemClaim` (see `docs/business/identity-and-access.md`), in which case
it self-confirms immediately.

Approving a contribution pays out points: `ContributionService.ConfirmContributionAsync`
(`ContributionService.cs:245-256`) looks up a per-`CategoryType` point value
via `IApplicationSettingsService` and calls
`ITransactionService.IncreaseBalanceAsync` with
`TransactionType.SuccessfulContribution`; deleting a contribution issues a
reversal via `TransactionType.DeleteContribution`
(`ContributionService.cs:357-370`). See `docs/business/payments-and-points.md`
for the ledger mechanics.

This same Contribution mechanism covers **school images** (upload as
contribution, `CreateSchoolImageContributionAsync`,
`SchoolService.cs:1034-1127`; confirm/reject at `:1128-1231`; removal is
itself a further contribution, `CreateRemoveSchoolImageContributionAsync`,
`:1355-1493`), **comments** (see below), and **reported issues**
(`CreateSchoolIssuesContributionAsync`/`ConfirmSchoolIssuesContributionAsync`,
`:1784-1895`).

An uploaded image auto-confirms without admin review if its EXIF GPS
coordinates are within 200 meters of the school's `Coordinates`
(`IsImageLocationNearSchoolAsync`, `SchoolService.cs:1091-1119`, using
MetadataExtractor to read the GPS EXIF tag), in addition to the
`AutoConfirmSchoolImage` claim path.

## Comments & ratings

`SchoolComment` (`src/Domain/Entity/SchoolComment.cs`) holds 8 required
sub-ratings — `ClassesQualityRate`, `EducationRate`, `ITTrainingRate`,
`SafetyAndHappinessRate`, `BehaviorRate`, `TuitionRatioRate`,
`FacilitiesRate`, `ArtisticActivitiesRate` — plus an `AverageRate` field
and `LikeCount`/`DislikeCount`. Notably, `AverageRate` is **not** computed
server-side from the 8 sub-ratings; `CreateSchoolCommentContributionAsync`
(`SchoolService.cs:791-846`) stores whatever `AverageRate` the client
submits (`:855`) — the server trusts the caller to have averaged the 8
values correctly. A unique index enforces one comment per user per school
(`SchoolComment.cs:79`). Comments go through the same
contribution-then-confirm flow as schools/images
(`CreateSchoolCommentContributionAsync` → `ConfirmSchoolCommentContributionAsync`,
`:870-911`), with duplicate-comment and duplicate-pending-contribution
guards (`:795-814`) and the same auto-confirm claim/setting escape hatch
(`:828-836`).

`GetSchoolRateAsync` (`SchoolService.cs:613-642`) aggregates all 8
sub-ratings (and `AverageRate`) across a school's comments for display.

## Tags, boards, images

`SchoolTag` and `SchoolBoard` (`src/Domain/Entity/SchoolTag.cs`,
`SchoolBoard.cs`) are simple many-to-many join entities (School↔Tag,
School↔curriculum Board) with uniqueness constraints preventing duplicate
associations. `SchoolImage` (`src/Domain/Entity/SchoolImage.cs`) records a
`FileId`, optional `Tag`, `IsDefault` flag, and an optional link back to the
`Contribution` that introduced it.

## Geospatial search

`GetSchoolsListAsync` (`SchoolService.cs:124-243`) uses NetTopologySuite's
`geography`-typed `Coordinates.Distance(point)` to compute distance from a
caller-supplied `Point` and orders results by that distance when no other
sort is requested (`:148, 154`) — it is a distance-ordered search, not a
radius-bounded ("within N km") query as currently implemented.

## Ranking / scoring system

`SchoolService.UpdateSchoolScoreAsync` (`SchoolService.cs:1895-1975`) is a
Hangfire job that recomputes an internal `Score` (0-150-ish, mixing average
review rating with completeness-of-listing signals like having a website,
photos, coordinates) purely to drive `CountryRank`/`StateRank`/`CityRank`
ordering — it is **not** meant to be a public "star rating." The public
rating is a separate `Rate` field (0-5, `null` if no reviews yet), computed
live from `AVG(SchoolComments.AverageRate)` and exposed on both the school
list and school details endpoints, decoupled from `Score`/the ranks. Full
history of this fix (previously a conflated/broken formula) lives in
`docs/business/school-scoring-analysis.md`; that document is the source of
truth for this topic and is not duplicated here.
