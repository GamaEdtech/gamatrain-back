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
| 3 | Results native, early, under their own switch (`WriteRoutes:Exams.Results`): start/submit and grading here, the result page, a user's results, an exam's participants; the copy then leaves attempts and answer sheets alone | to do |
| 4 | Admin exam and question management (refused until `WriteRoutes:Exams` is `Native`) | to do |
| 5 | The website's exam pages and components call this backend; the test maker's writes through it | to do |
| 6 | Final copy (data and files), then `WriteRoutes:Exams` `Native` | to do |

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
never runs twice at once, and **refuses once `Migration:WriteRoutes:Exams` is `Native`** (it would put gama-api's older
rows back over the changes made here).

- **Exams and questions**: every row's id and change date (`up_date`, else `subdate`) is read, then only the new rows and
  those whose change date differs from the copy's are read in full, by id. gama-api stamps every edit, so a change without
  a new date is not seen.
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
  `GetPublicExamTestAsync`): the questions in exam order with their correct option and images.
- Not native yet (answer `notNative`, keep their routes `Legacy`): results and start/submit (step 3) and the exam
  builder's calls with a gama-api token (the switch).
