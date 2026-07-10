# School scoring/ranking — resolved (2026-07-10)

> **Status: fixed.** The conflation described below has been resolved — the internal ranking
> signal (originally named `Score` throughout this document; **renamed to `RankScore`** on
> 2026-07-10, see "Resolution") still drives `CountryRank`/`StateRank`/`CityRank`, and the public
> rating is a genuine `Rate` field computed directly from `AVG(SchoolComments.AverageRate)`. As of
> the `RankScore` rename, the raw ranking number is **no longer exposed via the public API at
> all** — only `Rate` and the ranks are public. See "Resolution" at the bottom of this file. The
> rest of this document (including every `Score` reference below) is kept as the original analysis
> for historical context — it predates the `RankScore` rename.

Triggered by: API testing on seeded schools showed `score: 40` and
`reviewScore: 0.3636...` for every seeded row, which looked wrong.
Investigation shows two distinct concepts have been conflated in the code.

## Concept 1 — Ranking score (`Schools.Score`)

Purpose: order/rank schools relative to each other (country/state/city rank).
Not a user-facing rating.

- Computed entirely server-side by a batch recalculation:
  `SchoolService.UpdateSchoolScoreAsync` (`src/Application/Service/SchoolService.cs:1895-1966`).
- Runs as a Hangfire job (table evidence: `Job`, `JobParameter`, `JobQueue` in DB).
- Formula (raw SQL, one UPDATE over all schools):

  ```
  Score = AVG(SchoolComments.AverageRate) * 10      -- 0..50, 0 if no comments
        + 10  if Coordinates IS NOT NULL            -- 0 or 10
        + 25  if WebSite is set                      -- 0 or 25
        + 5   if Email is set                        -- 0 or 5
        + 5   if PhoneNumber is set                   -- 0 or 5
        + 5   if Address is set                        -- 0 or 5
        + ImageScore                                   -- 10 per image, capped at 50 (>=5 images)
  ```
  Max possible value: 50 + 10 + 25 + 5 + 5 + 5 + 50 = **150**.

- `CountryRank` / `StateRank` / `CityRank` are `DENSE_RANK()` over this same
  `Score`, partitioned by location — this is the actual purpose of the field.
- Exposed as-is (raw 0-150 points) via API as `score` in the school list
  (`SchoolInfoDto.Score`, `src/Core/Data/Dto/School/SchoolInfoDto.cs:17`).

## Concept 2 — Review/rating score (should be 0-5)

What the product actually wants to show users: "how parents rated this
school," on a familiar 0-5 star scale.

- Real source of truth for this exists already: `SchoolComments.AverageRate`
  (`src/Domain/Entity/SchoolComment.cs:74`), itself the average of 8 sub-ratings
  (Artistic Activities, Behavior, Classes Quality, Education, Facilities, IT
  Training, Safety & Happiness, Tuition Ratio), each validated `[Range(0, 5.0)]`
  (`ManageSchoolCommentRequestViewModel.cs`). So a genuine 0-5 per-school
  average is one `AVG()` away and is *already computed* inside
  `UpdateSchoolScoreAsync`'s `CommentAgg` CTE before being multiplied by 10 and
  folded into the ranking `Score`.

## The bug: `ReviewScore` is derived from the ranking `Score`, not from reviews

`src/Core/Data/Dto/School/SchoolInfoDto.cs:18`:

```csharp
public double? ReviewScore => Score.HasValue ? Score.Value * 5 / 550 : null;
```

This tries to rescale the 0-150-point ranking `Score` into a 0-5 range, but:

1. **The divisor is wrong even on its own terms.** Max achievable `Score` is
   150, not 550 (`550 / 5 = 110`, and 110 doesn't match any component of the
   formula). So `ReviewScore` can never approach 5 even for a school with a
   perfect review average, 5 images, coordinates, and full contact info —
   worked example: 50 + 10 + 25 + 5 + 5 + 5 + 50 = 150 → `150*5/550 ≈ 1.36`,
   not 5.
2. **It's the wrong input regardless of divisor.** `Score` mixes in things
   that have nothing to do with parent reviews — having a website, an email,
   a phone number, coordinates, or photos uploaded. A school with zero
   reviews but complete contact info gets `ReviewScore > 0`; this reads to
   users as "some parents rated this school," which isn't true.
   - Confirmed with the 100 seeded schools (no `SchoolComments`, no
     `SchoolImages`, no `Coordinates`, but `WebSite`/`Email`/`Phone`/`Address`
     all set): `Score = 0+0+25+5+5+5+0 = 40`, `ReviewScore = 40*5/550 ≈ 0.364`
     — exactly what the API returned, despite there being zero actual reviews.

This DTO-level `ReviewScore` flows straight through unchanged to the API
response: query → `SchoolInfoDto` (`SchoolService.cs:220`, `GetSchoolsListAsync`)
→ `SchoolInfoResponseViewModel` (`Score`/`ReviewScore` copied 1:1 in
`SchoolsController.cs:117-118`) → JSON `score` / `reviewScore` fields.

## Open questions for discussion (no code changed yet)

1. Should the public 0-5 rating come directly from
   `AVG(SchoolComments.AverageRate)` (null when a school has no comments),
   instead of being derived from the ranking `Score`?
2. Should the ranking `Score`/`CountryRank`/`StateRank`/`CityRank` stay
   exactly as-is (they seem fine for their actual purpose — ordering), just
   stop being the input to a public "review score"?
3. Naming: keep `score`/`reviewScore` field names in the API to avoid a
   breaking change, or rename to something less ambiguous (e.g. `rankingScore`
   vs `rating`)?
4. What should a school with zero reviews show — `null`, `0`, or omit the
   field?
5. Does the true 0-5 average need its own persisted/indexed column (for
   sorting "top rated" separately from "top ranked"), or is computing it live
   via a join acceptable?

## Resolution (implemented 2026-07-10)

Decisions made (product owner):
- New field name **`Rate`** (not a fixed `reviewScore`) — a deliberate rename to make clear it's a
  different concept from `Score`/the ranks, not just a corrected formula under the old name.
- `null` when a school has zero reviews (not `0`) — consistent with the nullable-double pattern
  already used for `Score`/`Distance` in these DTOs.
- Added to **both** the school list and the school details endpoint (details previously exposed no
  rating at all, only the rank fields).

Implementation: `Rate` is computed **live**, per request, as
`t.SchoolComments.Any() ? t.SchoolComments.Average(c => c.AverageRate) : (double?)null` — a LINQ
`.Average()` over the existing `School.SchoolComments` navigation, mirroring how `Distance` is
already computed live in the same query. No schema migration was needed and
`UpdateSchoolScoreAsync`/`Score`/the ranking job were **not** touched — ranking is still allowed to
use the review average as one of its ranking ingredients (mixed with completeness signals), it's
just no longer the *source* of the public rating.

Changed files: `SchoolInfoDto.cs` (removed the `ReviewScore` computed property, added a plain
`Rate` property), `SchoolDto.cs` (added `Rate`), `SchoolInfoResponseViewModel.cs` /
`SchoolResponseViewModel.cs` (added/renamed `Rate`), `SchoolService.cs` (`GetSchoolsListAsync` and
`GetSchoolAsync` projections), `SchoolsController.cs` (`GetSchools` and `GetSchool` mappings).

The old `reviewScore` field name no longer exists in the API — this is an intentional breaking
rename, not an oversight.

## Follow-up: `Score` renamed to `RankScore`, and removed from public output entirely (2026-07-10)

Once `Rate` existed as the correct name for the public rating, keeping the internal ranking value
named plain `Score` was itself confusing — it read as if it might be the same kind of thing as
`Rate`. Product decision: rename the internal ranking value to **`RankScore`** everywhere (DB
column, `School.RankScore` entity property, `UpdateSchoolScoreAsync`'s SQL, the `IX_Schools_Score`
index → `IX_Schools_RankScore`), via a hand-written EF Core migration
(`RenameScoreToRankScore`, `RenameColumn` + `RenameIndex`, matching this repo's existing
hand-authored rename-migration pattern, e.g. `20250913191122_Change Section To Board.cs`).

Additionally, the school **list** endpoint no longer returns the raw ranking number at all (it
was never returned by the details endpoint to begin with) — only `Rate` and
`CountryRank`/`StateRank`/`CityRank` are public now. `RankScore` is purely an internal
implementation detail of how those ranks get computed; it stays in the service-layer query
projection (for the existing dynamic sort-by-column-name feature) but is never mapped into
`SchoolInfoDto`/`SchoolInfoResponseViewModel`.

Separately, `HasScoreSpecification` (backing the list's `hasScore` filter) was found to be
**mislabeled**: it never referenced `Score` at all — its expression was always
`hasScore && t.SchoolComments.Any()`, i.e. it filters by whether a school **has reviews**. Renamed
to `HasRateSpecification` / `HasRate` (query param), matching what it actually does. Its
underlying boolean logic was **not** changed (only identifiers renamed) — note for anyone touching
this next: `HasRate=false` still matches zero rows (`false && x.Any()` is always `false`), the same
behavior the original `HasScore=false` had; this pre-existing quirk was out of scope for a pure
rename and was intentionally left as-is.
