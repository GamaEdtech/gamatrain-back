# Tutorials

Tutorials (gama-api's `dars`, "درسنامه") are being moved from gama-api into this backend, the same way as the past
papers (`docs/business/past-papers.md`): copy the data, copy the pictures, answer the website from the copy behind a
switch (`Shadow` first, sandbox first), then move editing here. See `docs/architecture/legacy-migration.md` for the switch.

## Steps

| Step | What | State |
|---|---|---|
| 1 | Tables `Tutorials` and `TutorialImages`, and a job that copies gama-api's rows into them (`ImportTutorials`) | done (sandbox: a sample of 200; production: all) |
| 2 | Copy the images (`uploads/tutorials/`) into the public storage's `Tutorial` container | done |
| 3 | The tutorial page, lesson tree, search tab and counts from the copy (`NativeTutorialSource`, `Migration:Routes:Tutorials.Website`); related tutorials too since 2026-11-23 | built, not switched |
| 4 | The website's tutorial page and lesson tree call this backend (`Search/tutorials/...`) | to do |
| 5 | Admin editing here (`TutorialAdminService`, `Migration:WriteRoutes:Tutorials`), then the final copy and gama-api's editing frozen | built, not switched |
| 6 | Topic matching managed here (`ImportTopicMatches`, `admin/topicmatches`, `Migration:WriteRoutes:Types.TopicMatches`) | built, not switched |

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
- The home page's and the search page's `tutorial` count is the routed tutorials search's total (since 2026-11-25; before,
  gama-api's `typesstats` with its count replaced, so it went missing when gama-api was down).

## Admin (`TutorialAdminService`, `admin/tutorials`)

Admins, and anyone granted the endpoints (the controller is `[DelegablePermission]`: user management, "Admin access",
Admin / Tutorials), manage the tutorials of the copy. Reads work at any time; **every change answers `notNative` until
`Migration:WriteRoutes:Tutorials` is `Native`**, and once it is the copy from gama-api refuses to run (it would undo the
edits), so switch it only after the final copy, with gama-api's editing frozen.

- **List and edit form**: any status, filters by status, board, grade, subject, topic, owner and text (title,
  description, keywords, or the id); the edit form gets the HTML with gama-api's image links on this backend's storage.
- **Saving** (gama-api's rules): title, content, board and subject are required; the board, grade, subject and topics must
  exist in the types copy; topics are stored as gama-api does (`id+id`) and the order is the last topic's list order. The
  HTML is sanitized; images are uploaded through `EditorImages` (WebP, public storage), never embedded. The description is
  the content's `h2`/`h3` headings joined by " | " (gama-api's `description_gen`, which the page description lists) unless
  one is given. gama-api's old editor conversions (Bootstrap panels to Semantic UI) are not kept: the editor no longer
  produces those classes.
- **A new tutorial** gets the next id above the highest one (gama-api's ids are kept), is owned by its author and waits as
  `Pending` unless a status is chosen. An edit keeps the status unless one is sent; only `Confirmed` is on the website.
- **Delete** removes the tutorial and its image rows for good (gama-api deletes too); the image files stay in storage.
  `Deactivated` takes it off the website without deleting.
- **Keywords**: copied from gama-api (`content_keywords` of tutorials, named by `keywords`) into `Tutorials.Keywords`
  (" | "-joined, 2000 characters); an admin can edit them or ask the AI (`POST admin/tutorials/{id}/keywords`), which uses
  this backend's AI provider (`IAiService`) with gama-api's prompt (its note 16) on the tutorial's text. The AI's
  learning clusters are only shown. The search's text filter matches the keywords too.
- A change drops the cached tutorial page and the lesson trees of its old and new subject; the search lists follow within
  their few minutes of cache.

## Topic matching (`TopicMatchAdminService`, `admin/topicmatches`)

gama-api links topics of different boards (`types_relation`, copied as `LegacyTaxonomyRelations`): a paper or tutorial list
for a topic, subject, grade or board also shows the content of the topics linked to it (`TopicMatching`). The links are
made by **topic matching**: an AI suggests matches between a GAMA topic and another board's topic (gama-api's
`topic_found_matches`), and a person approves them.

- **Which subjects are compared**: gama-api's fixed list of GAMA subjects and the other boards' subjects that correspond to
  each (`gamatrain_lessons_correlation`), copied as `GamaSubjects`. Matching is always between GAMA and one other board,
  never between two other boards. Changing the list is a code change, as on gama-api.
- **The copy** (`ImportTopicMatches`, by hand from `/hangfire`, read-only on gama-api) mirrors gama-api's rows into
  `TopicMatches` with their ids (967 in 2026-11: 408 approved, 559 waiting); a row gone from gama-api is removed here. It
  refuses to run once `Migration:WriteRoutes:Types.TopicMatches` is `Native`.
- **Reviewing** (`[DelegablePermission]`: user management, Admin / TopicMatches): the list by GAMA topic, most confident
  first, with each side's board, grade, subject and topic and the GAMA topic's first published tutorial; filters by topic
  (either side), GAMA or other subject, status. **Approve** adds the link (unless it is there either way round) and marks
  the match approved by the reviewer; **take back** removes the link and the match waits again; **remove** deletes a
  suggestion (gama-api's reject) and is refused for an approved one until it is taken back (gama-api allows it and leaves
  the link with no way to remove it); **add** records a match a person decided (`manual`, waiting).
- **AI suggestions** (`POST admin/topicmatches/suggest/{topic}`): for each corresponding subject, its topics (a season only
  against seasons, a topic only against topics) go to this backend's AI provider (`IAiService`) with gama-api's prompt
  (its note 17), all subjects at once. Only matches of 90+ confidence of a topic that was actually offered and not
  suggested already are saved, waiting. A subject the AI could not answer for is reported, not fatal.
- **Changes are refused (`notNative`) until both `Migration:WriteRoutes:Types` and `Types.TopicMatches` are `Native`**: an
  approval writes a link, and while the types are still copied from gama-api that copy would remove it. On the sandbox the
  types are already edited here, so to copy the matches there set `WriteRoutes:Types.TopicMatches` to `Legacy` for the
  run, then remove it. The link takes effect on the next list (the search lists' few minutes of cache).
