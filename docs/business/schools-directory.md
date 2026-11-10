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
  The review endpoint (`GET admin/schools/contributions/{id}`) returns the
  photo and the comment/ratings submitted with the school next to the field
  values (since 2026-11-10; before that it dropped them, so admins approved a
  new school without seeing what else it would publish).

`School` (`src/Domain/Entity/School.cs`) has no explicit approval-state
field of its own — the review/approval state lives entirely in
`Contribution`. Schools carry `SchoolType` (enum — `Public`, `Private`,
`Religious`, `FirstNation`, `PrivateNonProfit`, `Government`, `Community`;
`src/Domain/Enumeration/SchoolType.cs:9-27`), location FKs, contact info,
`Coordinates` (a `geography` `Point`), the internal ranking `RankScore`, `IsDeleted`
(soft delete), and `CountryRank`/`StateRank`/`CityRank`.

## The Contribution approve/reject workflow (generic)

`Contribution` (`src/Domain/Entity/Contribution.cs:16-48`) is a generic
moderation envelope reused across schools, images, comments, issues, and
posts: `CategoryType` (what kind of change), `Status` (state
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
`:1784-1895`). Confirming a reported issue **soft-deletes the school**
(`IsDeleted = true`), and `School` has a global `!IsDeleted` query filter, so
the issue lists (admin `issues/contributions` and the reporter's own
`{schoolId}/issues`) look the name up with
`GetSchoolsNameAsync(..., includeDeleted: true)`, and load the contribution
`Data` (the issue description) with `includeData: true`. Before 2026-11-07
neither did, so every confirmed issue showed no description and an
"unknown" school. The confirmation email's `[ISSUES]` placeholder is the
reported description (HTML-encoded); it used to repeat the school name.

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

A school's `LastModifyDate`/`LastModifyUserId` mark its latest activity:
they move when its details are edited, when an image is approved
(`UpdateSchoolLastModifyDateAsync`) and, since 2026-09-27, when a comment is
approved (`CreateSchoolCommentAsync`, in the same update as the rating
totals; `LastModifyUserId` is the commenter). A comment still pending review
doesn't count.

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

The list takes two area filters (`SchoolFilterRequestViewModel`, shared by the list, its facets and the map tiles):

- `Location` (latitude, longitude, radius in metres, `LocationIncludeSpecification`): schools within the radius, each
  with its `distance` from the centre; sortable by `distance`. Used for "near me" / "near my home".
- `Bounds` (south, west, north, east, `WithinBoundsSpecification`, since 2026-11-10): schools inside the box the map
  shows. It is an exact latitude/longitude range; a box up to 10° wide also gets a padded geography polygon test in front
  of it so SQL Server uses the spatial index (geography edges are great-circle arcs, hence the padding). North and east
  edges are exclusive, so a school on the edge of two boxes belongs to one.

## School finder: filters, facets, map, compare (since 2026-11-10)

The public finder (`/school` on the frontend) is built from four anonymous, edge-cached endpoints:

- **List** (`GET schools`): besides location, name, rating, photo, tuition and board filters it takes `SchoolTypes`
  (any of the named types) and `Tags` (facility tag ids, a school must have all; tag 9 "Transport Services" is the school
  bus). Each item carries `schoolType`, `reviewCount` (parents who rated it) and `tagIds`. "Top rated" sorts
  `rating desc, reviewCount desc`; that order is computed per request, not served by an index, so it is meant for a
  region or a radius, not the whole world.
- **Facets** (`GET schools/facets`): for the same filters, the total, how many have a photo / a rating / a tuition, counts
  per school type and per facility tag, and (with `IncludeRegions=true`) counts per country, state or city one level
  below the location filter, which the frontend shows as "Schools in nearby areas" links.
- **Map tiles** (`GET schools/map/{z}/{x}/{y}`): the schools in one web-map tile, with the list's filters except the
  area. Up to 120 schools a tile lists each one (id, name, position, rating, photo flag); above that it answers counts
  per cell of a 6×6 grid over the tile, each at the average position of its schools. `total` is always exact, so the map
  can say "all 38 schools in view" or "zoom in". The old map asked the list for at most 200 schools around a centre, so a
  busy area silently lost most of its schools (869 within 15 km of Dar es Salaam, 200 shown). Fixed tiles also mean the
  same URLs for every visitor looking at the same place, so Cloudflare answers repeats.
- **Compare** (`GET schools/compare?ids=..`, 1 to 4 schools): each school's details (as `GET schools/{id}`) and its
  parents' average rating per topic (as `GET schools/{id}/rate`), in one call. It does not count as a view.

## List order

The public school list's default ("Recently Updated, photos first") is `hasImage desc, lastModifyDate desc`: schools
with an approved photo first, each group by last activity (an edit, an approved image or comment; a school never
edited counts from its creation). The visitor can untick "photos first" to get plain recency. Both orders, alone and
within a country, read straight from an index on the computed columns `HasImage`/`LastActivityDate`
(`docs/database/schema.md`). Measured locally on 500k schools, the list query went from ~13,000 page reads and ~650 ms
CPU (sorting every school per request) to ~70 reads and ~1 ms (2026-11-09). "Has a photo" uses `DefaultImageId`, which
is set exactly when a school has an approved image (approving the first image sets it; removing the default promotes
the next one), so it needs no join to `SchoolImages`.

The ranks are not used for this order: they are per region (rank 1 exists in every country/state/city), so a list
across regions would interleave each region's top school, and they are refreshed only nightly.

## Ranking / scoring system

`SchoolService.UpdateSchoolScoreAsync` (`SchoolService.cs:1898-1975`) is a
Hangfire job that recomputes an internal `RankScore` (0-150-ish, mixing average
review rating with completeness-of-listing signals like having a website,
photos, coordinates) purely to drive `CountryRank`/`StateRank`/`CityRank`
ordering — it is **not** a public "star rating," and (as of 2026-07-10) it
isn't exposed via the public API at all. The public
rating is a separate `Rating` field (0-5, `null` if no reviews yet), the
average of approved comments' `AverageRate`, kept as running totals on the
school (`CommentsRatingSum`/`CommentsRatingCount`, added to as each comment is
approved; `UpdateSchoolCommentsRatingAsync` recomputes them from scratch once
after each app start) and exposed on both the school
list and school details endpoints, decoupled from `RankScore`/the ranks. Full
history of this fix (including the earlier conflated/broken formula and the
`Score` → `RankScore` and `Rate` → `Rating` renames) lives in
`docs/business/school-scoring-analysis.md`; that document is the source of
truth for this topic and is not duplicated here.
