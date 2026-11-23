# Exams: moving them off gama-api

Exams (gama-api's `azmoons`, "آزمون"), their question bank (`azmoon_tests`), the attempts (`azmoon_data`) and the
answers (`azmoon_results`) are being moved into this backend the same way as the past papers and tutorials
(`docs/business/past-papers.md`, `docs/business/tutorials.md`): copy the data and files, answer from the copy behind a switch
(`Shadow` first, sandbox first), then move the writes. See `docs/architecture/legacy-migration.md` for the switch.

## Decisions (2026-11-17)

- **Reads first, writes last.** The content team keeps creating questions and generating exams on gama-api for about ten
  days; a write here meanwhile could take an id gama-api hands out next. So the copy **repeats** (every 15 minutes) while
  the website's reads move to it, and every write stays on gama-api until one last full copy, then `WriteRoutes:Exams`
  goes `Native`.
- **Admin**: this backend's admin gets the full exam and question management (including writes and deletes), refused
  (`notNative`) until the write switch, like the tutorials'. gama-api has no admin endpoints to pass them to, so the content
  team keeps using gama-api's panel until then.
- **User side**: a teacher's or student's exam building (the test maker) goes through this backend, which passes the writes
  to gama-api (`IExamSource`, `Legacy`) until the switch, so gama-api keeps handing out the ids.
- **Results move early** (decided 2026-11-17): only creating questions and generating exams stay on gama-api; attempts and
  results get their own write switch, so the exam page's per-user state is exact and new attempts get ids above gama-api's.
- **Practice limit**: gama-api's free-question paywall is not reproduced for now: native answers are never locked.
- **Jalali dates** (`*_jalali`, gama-api's Persian month titles) mean nothing on this site and are not sent.
- **Files**: question and answer images (gama-api's `uploads/azmoonImages`, ~31,000 files) go to the public `ExamImage`
  container with their names, copied again before the switch for the images added meanwhile. An exam's own files
  (`uploads/azmoon/original` and `pdf`) are **not** copied: the export generates an exam's PDF (and Word/PowerPoint) on
  request (`GET exams/export`), so `FilePdf`/`FileOriginal` are kept as history only.

## Steps

| Step | What | State |
|---|---|---|
| 1 | Tables `Exams`, `ExamQuestions`, `ExamAttempts`, `ExamAnswerSheets` and the repeating copy (`ImportExams`); the files copied | built |
| 2 | The website's exam reads from the copy (`NativeExamSource`, `Migration:Routes:Exams.Website`): the exam page, a question's page, a random practice question, the search's exam tab and counts; the export (`Exams.ExamExport`) | built, not switched |
| 3 | Results native, early, under their own switch (`Exams.Games` and `Exams.ExamAnalysis`): start/submit and grading here, the result page, a user's results, an exam's participants; the copy then leaves attempts and answer sheets alone | built, not switched |
| 4 | Admin exam and question management (`ExamAdminService`, `admin/exams`, `admin/examquestions`; changes refused until `WriteRoutes:Exams` is `Native`) | built |
| 5 | The website's exam pages and components call this backend; the test maker's writes through it | to do |
| 5a | Native exam building for the MCP (`NativeExamSource.Builder.cs`, `NativePastPaperSource.Mcp.cs`) and the MCP sign-in on this backend's accounts (2026-11-23) | built, not switched |
| 6 | Final copy (data and files), then `WriteRoutes:Exams` `Native`; the copy then only brings rows gama-api makes later (add-only) | to do |

## What gama-api has (2026-11)

- **`azmoons`** (1,946 exams, 1,610 published): owner, board/grade/course/subject and topics (type ids), `type` (1 an
  online exam), `azmoon_type` (the exam type, a type id), the title, `tests` (the question ids in order, comma-separated),
  `tests_num`, `azmoon_time` (minutes), `code` (a short code), `file_original`/`file_pdf`, the thumbnail path, status,
  views, downloads, `metadata` (JSON, e.g. the paper it was built from), `bit_delete`, `subdate`/`up_date`.
- **`azmoon_tests`** (~75,900 questions, 53,640 confirmed): the question and four answers as HTML with their image files,
  the worked answer, `true_answer` (1-4), topics, level, status, usage counts, `up_date`.
- **`azmoon_data`** (1,796 attempts by 446 users): made at start (`status` 0), finished at submit (`status` 1, the score
  percent in `result_score`, the time in `submit_time`).
- **`azmoon_results`** (1,173): an attempt's answers, `tests` = "question:choice,...", linked by `start_id`.
- Not copied: `azmoon_tests_trueanswer_log` (answer-key history), `azmoon_packages` (old per-subject bundles), and
  `azmoon_insights`, `azmoon_results_enhanced`, `azmoon_tests_videos`, `azmoon_test_translations`, `azmoon_users_packages`
  (empty).

## The copy (`ImportExams`, step 1)

A Hangfire job every 15 minutes (`*/15 * * * *`), reading gama-api's database only, with the copy jobs' read-only account
(`Migration:LegacyDatabase`), and only the columns it copies. It does nothing without that setting (a developer machine),
never runs twice at once, and **once `Migration:WriteRoutes:Exams` is `Native` it only adds** (see "After the switch"
below): a full copy then would put gama-api's older rows back over the changes made here.

- **Exams and questions**: every row's id and change date (`up_date`, else `subdate`) is read, then only the new rows and
  those whose change date differs from the copy's are read in full, by id. gama-api stamps an edit from its site, but not
  everything: its admin panel confirms, hides or deletes an exam (`status`, `bit_delete`) and reviews a question (`status`)
  without a new date, and it raises an exam's participant count the same way. Those columns are read with the dates and
  compared to the copy's on every run, and a row where they differ is read in full too. Until 2026-11-21 only the count was
  compared, so an exam confirmed in the panel after its first copy (the exam batch publishes, the copy runs, the sub-admin
  confirms minutes later) stayed in the copy as status 7 and was missing from the website's exam list. The participant
  count is compared only while results are taken on gama-api; once they are taken here, the count is ours and an exam
  copied again keeps it.
- **Attempts and answer sheets** have no change date and are small, so they are read in full each run and only the rows
  that differ are written.
- Rows gama-api no longer has are removed here too; ids are gama-api's (`DatabaseGenerated.None`), so links keep working
  and new rows made here after the switch continue above them.
- Owners and users are linked by `CoreId` (the copy is the only place that reads it), the gama-api ids kept beside them.
- A test server copies a sample: `Migration:LegacyDatabase:MaxExams` (the sandbox: 200) keeps the newest exams, the
  questions they use, and their attempts and answers. Production leaves it unset and copies everything.

Nothing reads these tables yet.

## Native reads (`NativeExamSource`, step 2)

`IExamSource`'s native implementation answers the website's reads from the copy, in gama-api's shapes, where
`Migration:Routes:Exams.{Feature}` says so (`Website`, `ExamExport`; the MCP import's calls stay `Legacy`):

- **The exam page** (`GET exams/{id}`, `GetExamDetailsAsync`): published (status 1, 5 or 7) and not deleted, or the
  caller's own. The caller's own fields come from the signed-in user here (`ExamInformationRequestDto.UserId`; a source
  never reads the request): `examUserData` is their latest attempt (`id`, `status`), `owner`. gama-api's participation
  window: no dates is open any time (`freetime`); with dates it opens at the start and closes at the end, and a day after
  the end it is open again without dates. `price` is gama-api's: taking it is free up to 500 questions (then half a unit
  each), the PDF a unit per question, rounded to ten. The metadata's keys (the paper it was built from) are merged in.
  A view is counted per fetch that isn't the owner's (never in a shadow comparison). `fileSize` is 0 (unused).
  The topics are decoded as gama-api's `topicsDecoder` sends them (as on a tutorial's page); a missing `edu_month` is
  `"0"`. An exam whose gama-api owner has no account here shows the past papers' fallback author
  (`Migration:PastPapers:FallbackOwnerUserId`), on the page and in the lists, so it always has an author.
- **A question's page** (`GET Search/examTests/{code}`, cached; also by id): confirmed questions only; the `@@title@@`
  is taken out of the question as gama-api does; images from the `ExamImage` container; `paywall` never locked;
  `full=true` adds the topic's first tutorial.
- **A random practice question** (`GET Search/examTests/random?lesson=&topic=`, never cached): a confirmed four-choice
  question of the topic, else of the subject.
- **The search's exam tab** (`Search?type=azmoon`, also `exam`/`exams`) and every `exams`/`azmoon` count: published exams
  with gama-api's filters (`section`, `base`, `course`, `lesson`, `topic` with linked topics, imported topics for a board,
  grade or subject, `title`, `exam_type`, `examOpen`, `level`, `date_status`, `test_id`), sorted as gama-api's English
  search always does: year and month, then the latest change.
- **The export and the exam page's samples** (`GetExamInformationAsync`, `GetExamQuestionCountAsync`,
  `GetExamSampleTestsAsync`): the questions in exam order with their correct option and images. The samples take every
  question of the exam, whatever its status, in one query (gama-api's anonymous `examTests/{id}` gives only confirmed ones).
- The exam builder's calls (the MCP) are native too since 2026-11-23: see "Native exam building" below.

## Native results (step 3)

Taking an exam and its results are native in the same `NativeExamSource`, as gama-api's `Exam_lib` does them. They follow
the exam domain's `Games` feature (the submit and points already did) and `ExamAnalysis` (the analysis): **switching results
means setting `Routes` and `WriteRoutes` of both `Exams.Games` and `Exams.ExamAnalysis` to `Native` together**, after one
last `ImportExams`. From then on the copy leaves attempts and answer sheets alone (`WriteRoutes:Exams.Games` is `Native`):
they are written here, while exams and questions keep coming from gama-api until their own switch.

- **Start** (`GET exams/{id}/start`, a write): a published, open exam (gama-api's `isParticipable`: not deleted, public,
  started, not over); the caller's unfinished attempt is resumed, a new one gets the next id above gama-api's. Answers: the
  attempt id (`startID`), the exam, its questions in order **without** their correct or worked answer, and the seconds
  left (the exam's minutes since the attempt began, never past its end). gama-api's paid participation and pro paywall are
  not reproduced (participation is free up to 500 questions anyway).
- **Submit** (`POST exams/{id}/end`, unchanged for the website): the caller's own, unsubmitted attempt; answers 1-4 per
  question. A normal exam keeps "question:choice" for every question in order and is graded now: 3 per right answer,
  minus 1 per wrong one with negative marking (score type 2), as a percent of the exam's question count, one decimal. An
  exam of written answers keeps the answers as given (JSON) and scores 0 until its teacher scores it (gama-api has none
  taken yet). The attempt is finished (time taken, score), the exam's participant count goes up and each question's answer
  counts (`answers_stat`) too. A second submit answers with the attempt id (`alreadyParticipated`), as gama-api's refusal.
  Two submits sent at the same moment (a double click) save one result: the attempt's finish, its answer sheet, the exam's
  count and the answer counts go in one save, and the attempt's `Status` is a concurrency token, so only the submit that
  changes it from 0 is saved and the other one answers `alreadyParticipated`. Until 2026-11-17 both were saved (two
  sheets, counted twice). gama-api had the same race, so copied attempts can still have two sheets; readers take one.
- **Result page** (`GET exams/{id}/result`, the attempt id): the student's own, or the exam owner's. Per subject and in
  total: right, wrong, unanswered and the percent (same rule); every question with its correct and given answer; a timed
  exam's results only after its end (except for its owner). `rank` is `false` (gama-api ranks only with an analytics store
  it does not run). The points and the AI analysis read the same page (`ExamResultPageReader`, shared with gama-api's).
- **My results** (`GET exams/results`, filtered by `section`, `base`, `course`, `lesson` of the exam) and **participants** (`GET exams/{id}/participants`, the exam's owner only), newest
  first. gama-api's Iranian province/area/school fields are not sent.
- `ValidateTestAsync` (points for a single question) checks the answer against the copied question.

## Native exam building (step 5a, 2026-11-23)

The MCP (Claude, ChatGPT, Codex, the sandbox exam batch) builds exams on this backend once `Migration:Routes:Exams.Mcp`,
`WriteRoutes:Exams.Mcp` (and `Exams`), `Routes:PastPapers.Mcp` and `Routes:Types.Mcp` are `Native`.

- **Sign-in**: this backend's own password, teachers and admins only; the MCP access token carries this backend's bearer
  token (see `docs/api/authentication.md`, "MCP connector (OAuth)"). Nothing is sent to gama-api any more, so the MCP's
  `Legacy` routes no longer work: **switch the MCP routes together with the sign-in deploy.** Teachers who only ever
  had a gama-api password (the batch account and henry included) set one here first, with the forgotten-password flow.
- **Caller**: every request DTO carries `UserId` (the MCP's signed-in user, `ExamImportRules.CallerId`); each change
  needs the caller to own the exam or question (`accessDenied` otherwise).
- **Exams** (`NativeExamSource.Builder.cs`): one draft (status 6) per teacher (`alreadyInProgress`); a new id above the
  highest one here and a free 6-character code; `scoreType` 2 for negative marking; the paper it was built from in
  `Metadata.paperID` and the exam's id added to the paper's `CoreExamIds`. Setting the questions replaces the list
  (every id must exist). Publishing needs at least one question and makes it status 7 (public at once). Deleting hides it.
- **Questions**: new ones are the caller's, status 0 (waiting), HTML sanitized, a new id and code. An image left out
  keeps the old one. A question in someone's published exam, or that has answers, can't be deleted (`inUse`).
- **Images** (`UploadFileAsync`): PNG, JPEG, GIF or WebP up to 5 MB, stored for good in the public `ExamImage`
  container; its file id is what the question's image fields hold. The preview widget allows our CDN (`Core:Cdn`).
- **Publishing the questions**: an admin confirming an exam (`admin/exams/{id}/status` to 1, which the batch does as its
  confirmer) sets the exam owner's own questions in it from 0 or 2 to 1, so they get public pages. On gama-api a
  teacher's questions stayed "personal use" (2) for good, so the confirmed batch exams showed samples that answered 404.
  Questions of other authors in the exam are left as they are.
- **Papers** (`NativePastPaperSource.Mcp.cs`): find by board/grade/subject/classification/session, the directory search
  by title, a paper with its files, and a file's signed link. A link is given only when the file is free for the caller
  (their paper, an admin, no price, or bought here or on gama-api); the MCP never buys (`notPaid`).

## Exam builder API (2026-11-23)

`api/v1/examBuilder` (`ExamBuilderController`, teachers and admins) offers the same exam building over REST, for the
website's test maker and the exam bots (the GAMA bot built exams through gama-api's own `exams`/`examTests` endpoints):
the draft, its question list, publishing, questions and images, and the question bank (published questions and the
caller's own, with the correct option, filtered by board, grade, subject and topic). It goes through `IMigrationRouter`
as `Exams.TestMaker` (`IExamBuilderService`) and uses the native code above; its `Legacy` route sends the caller's
bearer token to gama-api, which only works for a gama-api JWT session, so switch `Exams.TestMaker` with the rest. The
bots confirm an exam with `PUT admin/exams/{id}/status` (an admin, or a user granted that endpoint).

A change to a question (`PUT questions/{id}`) rewrites all of its fields from the request, except images left out (kept)
and `viewType` left out (kept). So a client that fixes one thing reads the question first (`GET questions/{id}`, the
caller's own) and sends everything else back as it came; the batch account's fix scripts do this. `viewType` is gama-api's
`answer_view_type`, the options' layout (4 in one row, 2 per row, 1 per row), read by the Word export; whether the options
are images is `FileA`. Until 2026-11-23 a save wrote "image options ? 1 : 0" into it, so the 40 questions saved here that
day have 0 or 1; 0 means none, and the export then picks a layout from the options' length. A new question sent without
one has none, for the same reason.

## After the switch: the add-only copy

Once `WriteRoutes:Exams` is `Native`, `ImportExams` (still every 15 minutes) stops mirroring and only brings exams and
questions gama-api **created** after the last full copy, for whoever still adds them there:

- Set `Migration:LegacyDatabase:AddOnlyAfter:ExamId` and `:QuestionId` to gama-api's highest `azmoons.id` and
  `azmoon_tests.id` at the last full copy. Without them the copy refuses (`NotValid`): every row up to them is already
  here with gama-api's id.
- A newer row gets **a new id here** (the next free one; gama-api keeps handing out ids this backend may already have used)
  and keeps gama-api's in `LegacyId`. `LegacyId`'s filtered unique index stops a row from coming twice; a row that fails to
  save (its id was taken meanwhile) is tried again next run.
- A new exam's question list is mapped through those ids (an id up to the last full copy is kept as is, a newer one is
  looked up by `LegacyId`, an unknown one is dropped). Its code is kept unless one here already has it.
- A new question's images are fetched from gama-api's `uploads/azmoonImages` (`Core:Url`) into the `ExamImage`
  container; one that can't be fetched is left empty and logged.
- Later edits on gama-api to a row already here, attempts and answer sheets are not brought: those are written here.

## Admin (`ExamAdminService`, step 4)

Admins, and anyone granted the endpoints (both controllers are `[DelegablePermission]`: user management, "Admin access",
Admin / Exams and Admin / ExamQuestions), manage the copy. Reads work at any time; **every change answers `notNative` until
`Migration:WriteRoutes:Exams` is `Native`**: until then the content team edits on gama-api and the 15-minute copy brings it
here (a change made here would be overwritten).

- **Exams** (`admin/exams`): the list (any status; filters: status, board, grade, subject, exam type, owner, text = title,
  code or id; deleted ones on request), one exam with its questions in order; edit its settings (title, types, time, score
  type, year/month, dates), set its status (gama-api's codes: 0 waiting, 1 published, 2 rejected, 3 needs changes,
  5 cloned, 6 draft, 7 published by its teacher), replace its questions in a new order, delete (hide; results stay).
- **Question bank** (`admin/examquestions`): the list (filters: status, board, subject, topic, type, owner, text = the
  question, code or id), one question for the edit form (HTML and image links); add (owned by its author, waiting unless a
  status is sent, a new id above gama-api's and a 6-character code), edit, set its status (0 waiting, 1 confirmed,
  2 rejected), delete one no exam uses. gama-api's rules: the text, a known type (four-choice, descriptive, two-choice,
  true/false, blank, short answer) and the subject are required; a choice question needs every option and the correct one
  among them; the types must exist. The HTML is sanitized; new images go into the HTML through `EditorImages`.
