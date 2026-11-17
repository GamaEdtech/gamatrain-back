# Tutorials

Tutorials (gama-api's `dars`, "درسنامه") are being moved from gama-api into this backend, the same way as the past
papers (`docs/business/past-papers.md`): copy the data, copy the pictures, answer the website from the copy behind a
switch (`Shadow` first, sandbox first), then move editing here. See `docs/architecture/legacy-migration.md` for the switch.

## Steps

| Step | What | State |
|---|---|---|
| 1 | Tables `Tutorials` and `TutorialImages`, and a job that copies gama-api's rows into them (`ImportTutorials`) | done (sandbox: a sample of 200; production: all) |
| 2 | Copy the images (`uploads/tutorials/`) into the public storage's `Tutorial` container | done |
| 3 | The tutorial page, lesson tree, search tab and counts from the copy (`NativeTutorialSource`, `Migration:Routes:Tutorials.Website`); the related rows later | built, not switched |
| 4 | The website's tutorial page and lesson tree call this backend (`Search/tutorials/...`) | to do |
| 5 | Admin editing here (`Migration:WriteRoutes:Tutorials`), then the final copy and gama-api's editing frozen | to do |

Decisions (2026-11-17): editors are admins and anyone granted the permission (delegable, like the posts); the search
is a title and keyword search (gama-api's Elasticsearch ranking is not reproduced); keyword generation and any AI help
use this backend's own AI provider; bookmarks are not part of this move (the tutorial page's bookmark icon does nothing).

## What gama-api has

- **`dars`** (1,733 rows in 2026-11: 1,721 published, 12 waiting): owner (`user_`), `referee` (the referee who wrote or
  checked it), board / grade / course / subject (`section`, `base`, `course`, `lesson`, gama-api type ids, 0 for none),
  `topic` (several topics as `id+id`, usually a season and its chapter), `topic_order` (its chapter's list order),
  `title`, `description`, `content` (HTML), `content_size`, `status` (0 waiting, 1 published), `views`, `subdate`,
  `up_date`. Its `sub_topics` and `keywords` columns, and the `tutorials_sub_topics`/`tutorials_keywords` tables, are
  empty everywhere.
- **`tutorials_files`** (10,721 rows): the images uploaded for a tutorial's content, stored in gama-api's
  `uploads/tutorials/YYYY/MM/{tutorial}_{name}.ext` and linked from the content as
  `https://core.gamatrain.com/uploads/tutorials/...` (a few older ones point at `gama.ir`, a few are placeholders).
- **`keywords`** / **`content_keywords`** (240 / 97 rows): a small keyword list linked to content of several types.

Who may change a tutorial on gama-api: admins, sub-admins and referees (by permission); everyone may read a published
one. The new website has no page for writing tutorials; they are written in gama-api's own panel.

## What the website reads (gama-api's rules)

- **A tutorial's page** (`tutorials/{id}`): a published tutorial (any status for admins and referees), each fetch counts
  a view. Gamatrain sends the content with `loading="lazy"` on its images, the board, grade, course and subject titles,
  the subject's picture, the decoded topics (`topic_info`), `title_url`, and `pageTitle`/`pageDescription` built from
  the title and description.
- **The lesson tree** (`tutorials/lessonTree/{subject}`): the subject's seasons and chapters (from the types), each with
  up to 20 published tutorials of that topic (id and title), cached for 30 minutes.
- **The search** (the search page's tutorials tab and counts, through this backend's search proxy): published
  tutorials filtered by board, grade, subject or topic. A topic filter also takes the topics linked to it from other
  boards (`types_relation`, already copied as `LegacyTaxonomyRelations`); a board, grade or subject filter also takes the
  topics imported into it. Newest change first by default.
- **The first tutorial of each topic** (for a topic list): by `topic_order`, so a season gets its first chapter's.
- **Related content** rows (for example on a paper page) can include tutorials, from gama-api's recommendations.

## Copying them (`ImportTutorials`)

A Hangfire job, never scheduled (run it from `/hangfire`), like `ImportPastPapers`. It reads gama-api's database only,
with the copy jobs' read-only account (`Migration:LegacyDatabase`), and only the columns it needs:

- every `dars` row into `Tutorials` with **gama-api's id** (links keep working, new tutorials will continue above it),
  the type ids as they are (0 becomes none), topics and `topic_order`, the content as gama-api stores it, the status
  (unknown codes become `Pending`), the views and the dates (`up_date` as `LastModifyDate`);
- the owner: `OwnerUserId` is the user here whose `CoreId` is gama-api's `user_` (the copy is the only place that reads
  `CoreId`); `DisplayOwnerUserId` is that user, or the fallback user of the past papers
  (`Migration:PastPapers:FallbackOwnerUserId`) when the owner has no account here;
- every `tutorials_files` row of a copied tutorial into `TutorialImages`, keyed by gama-api's file id; rows of tutorials
  that were not copied, or with no file name, are skipped.

A test server copies a sample: `Migration:LegacyDatabase:MaxTutorials` (how many, the sandbox: 200) from
`TutorialsFromId` on (so the sample is the newest tutorials); images only of the copied ones. Production leaves both unset
and copies everything.

It can run any number of times: rows are added or updated by id, never duplicated. It **refuses to run once
`Migration:WriteRoutes:Tutorials` is `Native`**: tutorials are then edited here, and a copy would put gama-api's older
version back. The tables are read only by `NativeTutorialSource`, once its route is switched (below).

Stored content is kept exactly as gama-api has it. When the copy is served (step 3), its image links will be rewritten
to this backend's CDN and the HTML sanitized, as every stored HTML is (`docs/architecture/cross-cutting-concerns.md`,
"HTML sanitization"): one tutorial loads a script from `polyfill.io`, a domain that served malware in 2024.

## Serving them (`NativeTutorialSource`)

`SearchController` answers the website's tutorial calls through `SearchService`, cached like the paper calls, and each
follows `Migration:Routes:Tutorials.Website` (`Legacy`: gama-api as before; `Shadow`: gama-api's answer, compared with
the copy's; `Native`: the copy):

- `GET Search/tutorials/{id}`: gama-api's `tutorials/{id}`, published only. A view is counted once per cache refresh
  (never in a shadow comparison). The content's `core.gamatrain.com/uploads/tutorials/` links point at the `Tutorial`
  container and the HTML goes through `SanitizeHtml`, so the content differs from gama-api's on purpose; check what the
  sanitizer strips during the `Shadow` period. Dates are UTC only (gama-api's Jalali copies are not sent).
- `GET Search/tutorials/lessonTree/{subject}`: the subject's topics in list order, a season followed by its chapters
  (`tutorialsNum` is their total); each chapter (or a season without chapters) has up to 20 published tutorials, newest
  change first.
- The search page's tutorials tab (`Search?type=tutorial`, also `dars`) and every `tutorials` count: the copy's list with
  the filters `title` (title or description contains it), `topic`, `lesson`, `section`, `base`, `page`, `perpage`; sorted
  as gama-api (`sortby`, newest change by default). A topic filter takes the topics linked to it from other boards, a
  subject, board or grade filter the topics imported into it, through `TopicMatching` (shared with the past papers, over
  `LegacyTaxonomyRelations`); the topic matches still pending approval are not copied yet.
- The home page's `tutorial` count still comes from gama-api's `typesstats`, with its `tutorials` count replaced by the
  copy's once routed.
