# Exams, Curriculum & Content

Business logic: `src/Application/Service/BoardService.cs`, `GradeService.cs`,
`SubjectService.cs`, `TopicService.cs`, `QuestionService.cs`,
`ExamSerivce.cs` (filename typo in the repo — "Serivce" not "Service"),
`GameSerivce.cs`, `BlogService.cs`. Entities in `src/Domain/Entity/`:
`Board.cs`, `Grade.cs`, `Subject.cs`, `Topic.cs`, `Question.cs`,
`QuestionOption.cs`, `ExamSubmission.cs`, `TestSubmission.cs`, `Post.cs`,
`PostComment.cs`, `PostTag.cs`, `Tag.cs`.

## Curriculum hierarchy

- `Board` (`Board.cs:14-38`) is the top-level entity (`Id`, `Code`, `Title`,
  `Description`, `Icon`) with no parent — represents an education board/
  curriculum system.
- `Grade` (`Grade.cs:53-82`) belongs to exactly one `Board` via `BoardId`
  (`:73-75`), and has a many-to-many relationship to `Subject`.
- `Subject` (`Subject.cs:98-136`) is many-to-many with both `Grade` and
  `Topic` — the fluent config (`:119-133`) sets up two EF Core skip-navigation
  join tables (`SubjectGrades`, `SubjectTopics`). **There is no standalone
  `SubjectTopic.cs`/`SubjectGrade.cs` entity class** — these are implicit
  join tables, not first-class domain concepts, despite what the task
  framing might suggest.
- `Topic` (`Topic.cs:150-171`) is the many-to-many inverse side of Subject.

So a Subject can span multiple Grades and a Topic can belong to multiple
Subjects — it's a network, not a strict tree, below the Board level.
`BoardService`, `GradeService`, `SubjectService`, `TopicService` each expose
uniform list/get/upsert/remove methods with no special business rules
beyond CRUD, except `BoardService.SyncCoreBoardsAsync`
(`BoardService.cs:183`), which syncs boards from an external "Core"
provider.

## Questions

`Question` (`Question.cs:16-33`) is minimal: `Body` plus an `Options`
collection (`QuestionOption` — `Index`, `Body`, `IsCorrect`), stored as an
owned JSON column rather than a joined table. Notably, `Question` has
**no field linking it to a Topic, Subject, Grade, or difficulty level** —
none of those exist on the entity or its management DTO. `QuestionService.cs`
exposes CRUD plus `GetRandomQuestionsAsync` (`:51`, random selection via
`OrderBy(Guid.NewGuid())`); `RemoveQuestionAsync` (`:162`) guards against
deleting a question still referenced elsewhere.

## Exams: `ExamSerivce` is an export tool, not exam authoring

`ExamSerivce.cs` has exactly one public method, `ExportExamAsync`
(`:33`, matching `IExamService.cs:12`). It does not create or manage exams
locally — it fetches exam data from an **external "Core"/Game system** via
`ICoreProvider.GetExamInformationAsync` (using an `ExamId` + `SecretKey`)
and renders it to PDF/Word/PowerPoint. An exam, per that external DTO, is
composed of exam metadata (title, type, score type, time limit, test count)
plus a list of "Tests" (individual question items with up to 4 options) —
i.e. locally-authored `Question` entities are not the source for formal
exams; those live in the external system.

**All three formats are now free/open-source; no paid library anywhere in
this pipeline.** Word and PowerPoint are both built by hand-emitting native
OOXML directly (`DocumentFormat.OpenXml`) — no HTML-to-OOXML conversion
layer, no Spire. Pdf is the odd one out by design: it still renders from
real HTML through Chromium's print engine, because PDF is painted pixels,
not an editable document, so the "HTML can't produce a genuinely native
table" problem that motivated the Word rewrite doesn't apply to it.

**Word — `ExamWordDocumentBuilder.cs` / `ExamWordRichText.cs`.** Every
table, run, border, and shading is constructed directly against the OOXML
element tree; `HtmlToOpenXml.dll` was removed from the solution entirely
(it silently applied its own default `TableGrid` table style regardless of
CSS, mishandled bare-pixel widths, and generally couldn't produce a
genuinely native-quality Word table). `ExamWordRichText.ParseToParagraphsAsync`
walks a Core rich-text HTML fragment (via AngleSharp) and converts it
straight to OOXML runs/paragraphs — bold/italic/underline/sup/sub/color and
inline `<img>` (including MathJax-rendered formula images, see below) are
each translated to the equivalent native OOXML element. Two schema
correctness rules worth remembering if you touch this file again: (1)
every `w:tbl` needs an explicit `w:tblGrid` (one `w:gridCol` per column,
with a `w:w` dxa width if you want Word to actually honor the proportions
instead of autofitting to content) immediately after `w:tblPr`, or Word
silently repairs/collapses the table on open; (2) a table cell's content
must end with a paragraph, not a table — a cell whose last child is a
nested `w:tbl` renders as if it broke out of the cell. The options grid
(A/B/C/D) is a table nested inside the question's own content cell, each
option split into its own navy badge-letter cell plus its own content
cell (not a colored run + tab character faking a badge) — nested tables
need absolute `dxa` widths sized safely inside their container, since a
`Pct`-width nested table can resolve against the wrong base and overflow.

**PowerPoint — `ExamPresentationBuilder.cs`.** Also fully native OOXML
(PresentationML), one slide per question after a title/summary slide,
matching the same navy/yellow design as Word. PresentationML requires a
`ThemePart`/`SlideMasterPart`/`SlideLayoutPart` hierarchy before any slide
content can exist (Word's `WordprocessingDocument` needs none of that) and
uses absolutely-positioned shapes rather than flowing tables — natural for
a slide canvas. The options grid uses a real DrawingML table
(`a:tbl`/`a:tr`/`a:tc`, a different schema from Word's `w:tbl`) with the
same badge-cell + content-cell split as Word. **Known gap**: PowerPoint
slides use plain DrawingML text runs (`ExamRichTextPlain.ToPlainText`
strips all HTML down to plain text) rather than paragraph/run-level rich
text — a rendered MathJax formula becomes an `<img>` by the time this runs,
and a PowerPoint text shape can't host an inline image the way a Word run
can, so formula images are silently dropped from the PPTX export. Bold/
italic/color formatting is also not preserved (plain text only).

Question/option text can contain MathJax-style inline LaTeX (`$...$`),
confirmed from real exam data (e.g. exam 831/832 from Core) — this includes
non-trivial constructs like `\begin{gathered}...\end{gathered}` piecewise
functions, sometimes with stray `<br>` tags embedded mid-formula from the
source WYSIWYG editor. Before Word/Pdf render, question/option HTML is
passed through `IHeadlessBrowserRenderProvider`
(`HeadlessBrowserRenderProvider.cs`, Infrastructure layer), which runs the
*real* MathJax engine (not a partial LaTeX parser — those failed on the
non-standard constructs above) inside a headless Chromium tab
(PuppeteerSharp, `SupportedBrowser.ChromeHeadlessShell`) and swaps each
formula for a rendered PNG (`<img>`, base64 data URI for Word's native
image-embedding path; still an `<img src>` in the HTML Pdf renders). As
above, PowerPoint doesn't call formula rendering at all.

Pdf still builds from `BuildRenderedHtmlAsync()` against the
`exam.word.html` Handlebars template (the name predates the Word rewrite —
it's Pdf-only now), then calls
`IHeadlessBrowserRenderProvider.RenderPdfAsync`, which prints that HTML to
PDF using Chromium's own native print engine (`PdfDataAsync`,
`PrintBackground: true`, A4, 0.5in left/right margins, 0.8–0.9in top/
bottom) — real browser-quality rendering, reusing the same Chromium
instance already required for formula rendering rather than a separate PDF
library. A requested watermark is injected as a `position:fixed`
(deliberately, not `absolute` — Chromium's print engine repeats a
fixed-position element on every page) diagonal, semi-transparent `<div>`
before printing.

`IHeadlessBrowserRenderProvider` is a singleton service — launching
Chromium per request is far too slow — with a `SemaphoreSlim` capping
concurrent render pages (formula renders and PDF prints share the same
limit) to `Environment.ProcessorCount`; a burst of simultaneous export
requests queues rather than piling unboundedly onto the one shared browser
process. See `docs/deployment/overview.md` for the native library
dependency this introduces.

**Word/PowerPoint page-level infrastructure**, built directly against the
OOXML tree (no HTML involved at all): explicit A4 `SectionProperties`/
`PageMargin`; every Word `TableRow` marked `CantSplit` so a question can't
be separated from its own answer choices across a page break; a native
`HeaderPart`/`FooterPart` with a real `PAGE`/`NUMPAGES` `SimpleField` (Word
recalculates these itself as it paginates — not hardcoded page-count text);
an optional watermark rendered as a VML `v:textpath` shape folded into the
same header part (a section can only have one default header, so it can't
be a second one).

## ExamSubmission vs TestSubmission

These record two different kinds of user activity, both written from
`GameSerivce.cs`:

- **`TestSubmission`** (`TestSubmission.cs:14-45`: `UserId`, `TestId`,
  `SubmissionId`, `IsCorrect`, unique on `(UserId, TestId)`) — one row per
  **individual practice-question answer** ("TestTime" feature). Written by
  `GameSerivce.TestTimeAsync` (`:160-240`): blocks duplicate submission per
  `(UserId, TestId)`, validates the single answer live via
  `ICoreProvider.ValidateTestAsync`, and immediately awards/deducts a small
  fixed point amount (`TransactionType.CorrectTestTimeSubmission` /
  `IncorrectTestTimeSubmission`).
- **`ExamSubmission`** (`ExamSubmission.cs:14-48`: `UserId`, `ExamId`,
  aggregate `Valid`/`Invalid`/`NoAnswer` counts, unique on
  `(UserId, ExamId)`) — one row per user per **formal, multi-question exam**.
  Written by `GameSerivce.ExamPointsAsync` (`:242-316`): blocks duplicate
  submission per `(UserId, ExamId)`, fetches an aggregate result (not
  individual answers) via `ICoreProvider.GetExamResultAsync`, stores the
  valid/invalid/no-answer tally, and awards points proportional to
  `Valid`/`Invalid` counts (`TransactionType.CorrectExamSubmission` /
  `IncorrectExamSubmission`).

This reading is inferred from field shapes and call-site usage in
`GameSerivce.cs` — neither entity is documented in code, so treat "formal
exam" vs "practice question" as the best available interpretation, not a
stated fact.

## Blog

`BlogService.cs` (contract `IBlogService.cs:14-41`) manages `Post`,
`PostComment`, `PostTag`, `Tag`. It uses the **same Contribution-based
moderation pattern** as the schools directory (see
`docs/business/schools-directory.md`): `ManagePostContributionAsync`
(`BlogService.cs:203`) submits a post as a `Contribution`
(`CategoryType.Post`, `Status.Draft`/`Review`, `:293`), auto-confirmed if
the user holds `SystemClaim.AutoConfirmPost` or the `AutoConfirmPosts`
setting is on (`:304-312`), otherwise requiring
`ConfirmPostContributionAsync` (`:602-668`) to materialize the real `Post`.
Comments follow the same pattern (`CreatePostCommentContributionAsync` /
`ConfirmPostCommentContributionAsync`, `CategoryType.PostComment`).
Comment submission is gated by captcha at the controller layer
(`Presentation/Api/Controllers/BlogsController.cs:514-517`, via
`IGlobalService.VerifyCaptchaAsync`), not inside `BlogService` itself.
Admins can also bypass contribution entirely with `ManagePostAsync`
(`:324`, direct upsert).

Entities: `Post` (`Post.cs:16-82`: `Slug`, `Title`, `Body`, `ImageId`,
`PodcastId`, `LikeCount`/`DislikeCount`, `VisibilityType`, `Keywords`,
`ViewCount`); `PostComment` (`PostComment.cs:15-45`: one comment per user
per post, enforced by a unique index at `:42`); `PostTag`
(`PostTag.cs:14-33`, join entity, unique on `(PostId, TagId)`); `Tag`
(`Tag.cs:16-42`: `Name`, `TagType`, unique on `(TagType, Name)`).

`TagType` (`src/Domain/Enumeration/TagType.cs:6-24`): `School`, `Post`,
`Feature` — scopes what a tag can be attached to. `CategoryType`
(`src/Domain/Enumeration/CategoryType.cs:54-85`) is the broader vocabulary
used by the Contribution system across both schools and blog content
(`School`, `SchoolComment`, `SchoolImage`, `Post`, `SchoolIssues`,
`RemoveSchoolImage`, `PostComment`), each carrying an
`ApplicationSettingsName` used to look up its point-reward value. `ContentType`
(`src/Domain/Enumeration/ContentType.cs:31-47`: `PastPaper`, `Test`) feeds
the `DownloadPastPaper`/`DownloadTest` point-spend transaction types (see
`docs/business/payments-and-points.md`).
