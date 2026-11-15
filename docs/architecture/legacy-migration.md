# Moving off gama-api: the migration switch

This backend still gets part of its data from gama-api, the legacy backend, through `ICoreProvider`. That part moves
here one domain at a time. This page describes the mechanism every domain uses, so a domain can be built, filled and
checked while gama-api keeps answering, and switched (or switched back) by a setting.

## Rules

1. **gama-api answers until a domain's data and files are fully copied and checked.** New code is built first and gets
   no traffic.
2. **The switch is a setting, not a deploy.** Turning a domain on, or back off, changes `Migration:Routes` only.
3. **One writer at a time.** While gama-api answers, it is the only side that is written to.
4. **Same IDs and URLs.** A copied record keeps its gama-api id.
5. **A domain's proxy methods are deleted only after its switch has been on and stable.** `ICoreProvider` itself goes
   last, after sign-in has moved.

## Order (changed 2026-11-15)

Past papers move first, data before code: every step below is either a copy or code that gets no traffic until the
switch, and each switch can be turned back off.

| # | Past papers step | State |
|---|---|---|
| 1 | Copy the paper files into the private bucket (`PastPaper` container), keeping gama-api's paths | done (127,653 files, verified) |
| 2 | Copy the papers into `PastPapers`/`PastPaperFiles` (`ImportPastPapers`) | done in production |
| 3 | Copy what users bought on gama-api into `ContentPurchases` (`ImportPastPaperPurchases`) | done in production |
| 4 | This switch (no behaviour change), and the copy of gama-api's lookup lists (types), which the paper page needs | in review |
| 5 | gama-api's paper price rule, reproduced exactly (done); file sizes checked (done); public thumbnails and subject pictures in the public bucket (to do) | partly done |
| 6 | Native paper page, search and MCP reads (`IPastPaperSource`), checked in `Shadow` against gama-api | to do |
| 7 | Native downloads: price, charge and the paid-file record on this side (`ContentPurchases`), signed private links | to do |
| 8 | Teacher submit/edit and admin review, with the frontend; a final re-copy of papers, files and purchases; switch | to do |

The other domains (tutorials, exams, finance, identity) follow the same pattern afterwards.

## Domains and routes

`MigrationDomain` (`Domain/Enumeration`) lists the domains in the order they move: `Types`, `PastPapers`, `Tutorials`,
`Exams`, `Finance`, `Identity`.

`MigrationRoute` says where a domain is served from:

| Route | Who answers the caller | Native source |
|---|---|---|
| `Legacy` (default) | gama-api | not called |
| `Shadow` | gama-api | called afterwards with the same call, only to compare |
| `Native` | this backend | answers |

A missing, empty or misspelled setting means `Legacy`. `Native` without a registered native source also falls back to
gama-api, with a warning in the log.

## How a call is routed

Each domain has a source interface in `Infrastructure/Interface` that extends `IProvider<MigrationRoute>`
(`ITaxonomySource` for `Types`). `CoreProvider` implements it as the `Legacy` source; the native implementation, when it
exists, reports `MigrationRoute.Native`. Callers never use a source directly. They call
`IMigrationRouter.RunAsync` (`Application/Service/MigrationRouter.cs`):

```csharp
var boards = await migrationRouter.Value.RunAsync<ITaxonomySource, IEnumerable<KeyValuePair<int, string?>>>(
    MigrationDomain.Types, MigrationFeatures.BoardSync, t => t.GetBoardsAsync());
```

### Reads and writes

A call that only reads uses `RunAsync` and follows `Migration:Routes`. A call that changes something, or that must not
run twice (saving a question, publishing an exam, a download link that charges), uses `RunWriteAsync` and follows
`Migration:WriteRoutes`, which knows only `Legacy` and `Native`. So a domain's reads can move first while gama-api is
still the only side written to, and a write is never repeated for a comparison.

The second argument names the calling feature (`MigrationFeatures`), so one feature can be switched before the rest of
its domain.

A source is also called outside a request (the shadow comparison runs after the caller has its answer, in its own
scope). So a source takes everything it needs from its arguments, never from the current HTTP request, and a call
routed through the switch must have no side effects on a native source while the route is `Shadow`.

### Moving a domain's methods

When a domain starts, its methods leave `ICoreProvider` for the domain's source interface, and its callers go through
the router. Nothing changes for users: the route is `Legacy`. `ICoreProvider` shrinks with every domain until it is
empty.

Done so far:

| Domain | Source interface | Callers routed | Native source |
|---|---|---|---|
| Types | `ITaxonomySource` | board sync (`BoardService.SyncCoreBoardsAsync`), MCP exam import options | `NativeTaxonomySource`, from `LegacyTaxonomyItems` (see below) |
| PastPapers | `IPastPaperSource` | website paper list and paper page (`SearchService`), the exam page's paper link, MCP exam import (find and read papers, file links) | not built yet |
| Exams | `IExamSource` | public exam page, exam export, exam analysis and results page, points for tests and exams (`GameService`), the exam submit proxy (`EndExamAsync`, a write), MCP exam import (drafts, questions, publish) | not built yet |

Still on `ICoreProvider`: the cross-type search (`SearchAsync`) and the sign-in bridge (`Legacy*`, the dashboard),
which move with the `Identity` domain at the end. Tutorials have no calls in this backend yet (the website reads them
from gama-api directly), so there is nothing to route until their endpoints are built here. Past paper downloads go
through `IContentDeliveryProvider`, which already has its own source key (`ContentSource`).

## Types: the copy of gama-api's lookup lists

The paper page shows titles (board, grade, subject, paper type, extra-file type), subject pictures, and prices that use
an extra-file type's own multiplier; all of it lives in gama-api's lookup lists (`type_slave`). They are copied with
gama-api's ids into `LegacyTaxonomyItems` (one row per item) and `LegacyTaxonomyItemParents` (one row per parent key;
see `docs/database/schema.md`). The tables keep gama-api's model on purpose; it is redesigned after the move.

- **How the shape was found.** From gama-api's code (`type_slave`, its `types/list` endpoint and the helpers behind it),
  checked against its public `types/list` answers.
- **The copy.** `ILegacyTaxonomySyncService.SyncAsync` walks gama-api's public `types/list` from the top: the paper
  types, the classifications, then each board's classifications, courses and grades, each grade's subjects (also per
  course), and each subject's topics. One list at a time with a short pause, read only. It adds new items, updates
  changed ones, and marks what gama-api no longer lists as deleted (rows are never removed). If any list fails to read,
  it changes nothing, so a partial read can never delete data. It always reads gama-api directly, whatever the route.
  Run it by hand from `/hangfire` (`SyncLegacyTaxonomy`, never scheduled); it is safe to run again.
- **The native source** (`NativeTaxonomySource`) answers from the copy with gama-api's rules: deleted items are left
  out; a list is ordered by its list order (then id); "under a parent" means the parent is one of the item's parent
  keys; a subject's key is `course,grade` with course 0 when none is given; a list that needs a parent fails without
  one. It gets no traffic while `Migration:Routes:Types` is `Legacy`.
- **Not copied:** gama-api's other lookup lists (areas, blog categories, file types and so on), which no caller reads.

To switch Types later: run the copy, set `Migration:Routes:Types` (or `Types.Mcp`) to `Shadow`, watch the comparison,
then `Native`. Until gama-api stops being where the lists are edited, the copy has to run again after they change.

## The shadow comparison

With `Shadow`, the caller gets gama-api's answer unchanged. The native source is then called with the same arguments
and the two answers are compared as JSON (`MigrationShadowComparison`): property order is ignored, list order is not.

- A difference is logged as a warning with the domain, the feature and the place of the first difference (a JSON path).
  **Values are never logged**, because an answer can hold a user's data.
- Counters per domain and feature (matched, mismatched, failed, skipped) are kept in memory for the life of the process:
  `IMigrationRouter.GetShadowStatistics()`.
- At most four comparisons run at once. Past that, calls are answered without being compared (counted as skipped), so a
  slow native source cannot slow the site down.
- `Migration:ShadowSamplePercent` compares only that share of the calls.

A domain is ready to switch when its comparison shows no differences over a period the team agrees on.

## Settings

```json
"Migration": {
  "Routes": {
    "Types": "Legacy",
    "Types.Mcp": "Shadow"
  },
  "WriteRoutes": {
    "Exams": "Legacy"
  },
  "ShadowSamplePercent": 100
}
```

`Routes:{Domain}` sets a domain's reads and `WriteRoutes:{Domain}` its writes; `{Domain}.{Feature}` overrides either
for one feature. The tracked
`appsettings.json` keeps every domain on `Legacy` (`MigrationWiringTests` checks this); a switch is made in the
environment's own settings.

## Tests

`MigrationRouterTests` cover the route parsing, the fallbacks, that `Legacy` never touches a native source, that
`Shadow` returns gama-api's answer, that writes follow their own route and are never compared, and the comparison. `MigrationWiringTests` cover the real container.
