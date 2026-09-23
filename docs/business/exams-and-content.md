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
`ICoreProvider.GetExamInformationAsync` (using an `ExamId` + `SecretKey`,
forwarded to gama-api as `Authorization: Bearer {SecretKey}` — despite the
name, this is the caller's own gama-api legacy JWT, not a static API secret;
a naming holdover from when this backend and gama-api's were fully separate
systems with no shared server-to-server credential. `ExamsController.Export`
now sources it from the standard `Authorization` header via
`TokenAuthenticationHandler.GetTokenFromHeader`, same as `DownloadsController`
— no more separate `SecretKey` header) and renders it to PDF/Word/PowerPoint.
An exam, per that external DTO, is
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
nested `w:tbl` renders as if it broke out of the cell. A third, found while
building the layout below: within `w:tcPr`, `w:vMerge` must come *before*
`w:tcBorders`/`w:vAlign` (ECMA-376 `CT_TcPr` child order) — reversed, Word's
own validator rejects the cell with "unexpected child element vMerge".

**Word question layout — one shared table for the whole exam (2026-09-22
redesign).** Every question is a group of rows in **one continuous table**
(`BuildSharedQuestionTable`/`AppendQuestionAsync`), not a separate table per
question — matches the reference template's own real structure exactly,
confirmed by reading its `document.xml` cell-by-cell (a single 31-row table
for its 5 sample questions, not 5 tables). An earlier revision of this file
gave every question its own top-level table plus a nested table for its
options, with a doc comment here claiming that was "verified against a
genuine Word document" to be necessary — that claim didn't survive checking
the reference's actual file and has been corrected. The shared table has a
single `tblGrid`: a narrow `QuestionNumberColumnDxa` badge column, then
`ContentFineColumnCount` (16) equal-width fine columns. Every layout —
including the options grid, which is no longer a nested table either —
expresses itself purely via `w:gridSpan` combinations over those same fine
columns, the same technique the reference itself uses (its own real grid has
11 columns of uneven, hand-tuned widths fitted to its 5 fixed sample
questions; this one uses 16 *equal* columns, general enough for arbitrary
real content). Each question is: two header rows (question number + text,
the text cell `w:vMerge`-spanned across both so a wrapping question grows
into the second row instead of being clipped), then its option rows, then
two thin spacer rows — the first carrying the navy separator as its own
`w:tcBorders` bottom border (color `SeparatorNavy` = `#002060`, `single`,
size 8 = 1pt — the reference's own real measured value, not the earlier
approximate brand navy), the second blank padding beneath it (skipped for
the exam's very last question). Letting real content flow inside one
ordinary table, rather than a table per question, is also what lets a
question that lands at a page boundary split and continue naturally onto the
next page — confirmed live against real 40-question exam 1061, where a
question's own text wraps across a page break exactly like reference-style
running text.

Every question's options are normalized onto one of four
`ExamWordDocumentBuilder.QuestionLayoutType` values (`TextHorizontal`,
`Text2x2`, `TextVertical`, `ImageOptionsHorizontal`), each with its own
dedicated `Build*OptionRowsAsync` method returning the row(s) to append,
dispatched from `BuildOptionRowsAsync`. `ClassifyLayout` picks the type per
question, **preferring Core's own real fields over guessing from content
shape** — confirmed by live-querying `GET Core:ExamInfo` for exams
831/832/1061/2037 (64 real MCQ questions) with a real bearer token during
this redesign:

- `TestDto.TestImageAnswers` (Core's `testImgAnswers` bool) → real "all four
  options are images" signal → `ImageOptionsHorizontal`. Replaces guessing it
  from every option's text being blank.
- `TestDto.AnswerViewType` (Core's `answer_view_type` string) was seen live
  as **only ever `"1"`, `"2"`, or `"4"`** across the whole sample, and
  cross-referencing against those questions' real option text lengths lines
  up exactly with **options column count**: `"4"` → `TextHorizontal` (four
  across one row, badge=1 fine column + option=3 per pair), `"2"` →
  `Text2x2` (two per row, badge=1 + option=7 per pair), `"1"` →
  `TextVertical` (stacked, badge=1 + option=15). No value implying an
  image-specific layout was ever observed — `TestImageAnswers` and
  `QuestionFile` are the independent real signals for that. The old
  text-length heuristic (`LongOptionTextThreshold` 28 chars /
  `ShortOptionTextThreshold` 12 chars) is kept only as a fallback for when
  `AnswerViewType` is null/unrecognized.
- `TestDto.QuestionType` (Core's per-test `type`: `"fourchoice"`/
  `"descriptive"`) is now `HasOptions`'s preferred signal too, replacing the
  blank-option-fields guess (kept as fallback for an unrecognized value).

**A question's own shared image is orthogonal to the column-count layout,
not a fifth layout of its own** — confirmed live against real exam 1061
(Cambridge A-Level Physics past paper), where a `QuestionFile` commonly
accompanies *any* of the three text layouts (a `TextHorizontal` question with
a small reference diagram/table beside it was the most common real case, not
the stacked layout an earlier, single-image-implies-vertical design
assumed). `LoadSharedQuestionImageAsync` loads it once; when present, every
text layout's badge/option split shrinks to free the last
`ImageFineColumnSpan` (4) fine columns for it — merged via real `w:vMerge`
across however many option rows that layout has (none needed for
`TextHorizontal`'s single row, `w:vMerge` across 2 rows for `Text2x2`, across
4 for `TextVertical`), never four separately-placed pictures or a picture
floated beside the whole question. A descriptive question (`!TestDto
.HasOptions`) with its own `QuestionFile` has no options layout to attach it
to; its image is simply centered in its own row
(`BuildDescriptiveImageRowAsync`). The badge fill color (`BadgeGray` =
`#EDEDED`) also matches the reference's own real measured value (was an
approximate `#E7ECF2` before). The badge itself is a small **nested,
auto-sized 1×1 table** (`BuildNumberBadgeChip`), not shading applied directly
to the outer grid cell — a tight grey chip around the number, with real
`w:tcMar` padding on all 4 sides (`BadgeChipHorizontalPaddingDxa`/
`BadgeChipVerticalPaddingDxa`), centered (`w:jc` on the nested table) inside
the wider/unshaded outer badge cell. This replaced an earlier run-level
`w:shd` approach (`CreateRun`'s `shadeHex` parameter): a run-level shade also
avoids covering the whole cell, but OOXML gives it no padding concept at
all — it paints tight to the digit's own glyph bounding box, so a requested
padding around the number couldn't be expressed that way. `BuildQuestionNumberCell`/`BuildOptionBadgeCell`
both build their chip through `BuildNumberBadgeChip`; a blank badge cell (a
header continuation row, or the number column on an option row) gets no
nested chip table at all, since there's no digit to put one around.

**Answer Key page (`AppendAnswerKeySection`/`BuildAnswerKeyBlock`).** A final
page, one "mini answer-sheet" table per 10 questions (`AnswerKeyRowsPerBlock`),
up to 4 of those blocks side by side per row (`AnswerKeyBlocksPerRow`) before
wrapping to a new row of blocks — matching the reference template's own
layout exactly (verified 2026-09-23 against `Temp.docx`, whose answer-key
page arranges the same 10-question blocks 4-across). Each block is its own
small bordered table (yellow `AnswerKeyHeaderYellow` header row with option
labels 1-4, then one row per question with a filled/empty square per
option). **Fixed 2026-09-23**: the outer per-row table's cell held the
nested block table with no trailing paragraph after it — the one place in
this file that missed the "a table cell's content must end with a
paragraph, not a table" rule already followed everywhere else (see
`BuildNumberBadgeChip`'s own cell) — and LibreOffice's renderer respected
the structurally-correct 4-cells-in-one-row OOXML but visually stacked every
block after the first onto its own line instead of placing it beside its
row-mates. The row's own grid width also still used a pre-margin-change
literal (`9026`, from the 1440-dxa-margin era) instead of the shared
`PageContentWidthDxa`; fixed to use it, matching the same stale-constant
class of bug already fixed for the header/background earlier in this
redesign. **Known limitation, unchanged**: every square renders
empty/unmarked, since `ExamInformationResponseDto.TestDto.CorrectOption` is
always null today — Core doesn't return the correct answer yet. Once Core
adds that field and it's threaded through, the marks appear with no layout
changes needed.

**PowerPoint — `ExamPresentationBuilder.cs`.** Also fully native OOXML
(PresentationML), one slide per question after a title/summary slide,
matching the same navy/yellow design as Word. PresentationML requires a
`ThemePart`/`SlideMasterPart`/`SlideLayoutPart` hierarchy before any slide
content can exist (Word's `WordprocessingDocument` needs none of that) and
uses absolutely-positioned shapes rather than flowing tables — natural for
a slide canvas. The options grid uses a real DrawingML table
(`a:tbl`/`a:tr`/`a:tc`, a different schema from Word's `w:tbl`) with the
same badge-cell + content-cell split as Word. **Known gap, unchanged**:
PowerPoint slides still use plain DrawingML text runs (`BuildRichParagraphs`
walks the HTML but only recognizes plain text and formula markers, not
bold/italic/color) — formatting other than formulas is not preserved.

## Formulas: native OOXML Math (`m:oMath`), not rasterized images, for Word/PowerPoint

Question/option text can contain MathJax-style inline LaTeX (`$...$`),
confirmed from real exam data (e.g. exam 831/832 from Core) — this includes
non-trivial constructs like `\begin{gathered}...\end{gathered}` piecewise
functions, sometimes with stray `<br>` tags embedded mid-formula from the
source WYSIWYG editor.

**Pdf** still renders formulas as images: `IHeadlessBrowserRenderProvider
.RenderFormulasAsync` runs the *real* MathJax engine (not a partial LaTeX
parser — those failed on the non-standard constructs above) inside a
headless Chromium tab (PuppeteerSharp, `SupportedBrowser.ChromeHeadlessShell`)
and swaps each formula for a rendered PNG (`<img>`, base64 data URI), since
Pdf's HTML+Chromium-print pipeline has no other way to place a formula.

**Word and PowerPoint use `RenderFormulasToOmmlAsync` instead**, producing a
real, editable `m:oMath` equation object rather than a picture:
1. The same MathJax render already generates a hidden MathML annotation for
   accessibility (`<mjx-assistive-mml>`, `assistiveMml:!0` by default in the
   vendored `tex-svg.js`) — no separate MathJax bundle/render pass needed.
2. That MathML is converted to OOXML Math via the vendored
   `wwwroot/lib/mathml2omml/mathml2omml.js` (npm `mathml2omml` 0.5.0,
   LGPL-3.0-or-later, a from-scratch reimplementation — **not** a copy of
   Microsoft's own `MML2OMML.xsl`, which several other open-source projects
   explicitly avoid bundling since it isn't safely redistributable). Runs
   inside the same headless Chromium page as MathJax, so no new .NET/NuGet
   dependency. Two real bugs were found and patched in the vendored copy
   (see its header comment) by validating actual output against
   `DocumentFormat.OpenXml`'s `OpenXmlValidator` — "well-formed XML" and
   "schema-valid OOXML" are different checks, and only the latter reliably
   predicts whether Word/PowerPoint will show a repair prompt.
3. **Word** (`ExamWordRichText`): the `<m:oMath>` fragment becomes a direct
   `OfficeMath` sibling of `w:r` runs within the paragraph — inline with
   surrounding text, same as Word's own equation editor.
4. **PowerPoint** (`ExamPresentationBuilder.BuildRichParagraphs`): unlike
   Word, DrawingML's `a:p` has no slot for a bare `m:oMath` — PowerPoint
   2010+ represents slide equations via an `mc:AlternateContent`/`a14:m`
   markup-compatibility wrapper instead (`mc:Choice` requiring the `a14`
   extension, `mc:Fallback` a plain placeholder run for older consumers).
   Each formula becomes its own dedicated paragraph rather than staying
   inline mid-sentence, since AlternateContent isn't valid mixed into a
   single paragraph alongside plain `a:r` runs — a real formula on its own
   line is still a large improvement over the previous "silently dropped
   entirely" behavior.
5. Both paths fall back to the same rendered-PNG `<img>` PDF uses, per
   formula, if the MathML→OMML conversion throws — one bad formula degrades
   to an image rather than failing the whole export.

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

`ExamWordDocumentBuilder`/`ExamPresentationBuilder`'s shared
`EmbedImageFromSourceAsync` fetches a question/option image (`QuestionFile`/
`OptionXFile`, or an `<img src>` from rich text) via a plain `HttpClient`
with no `BaseAddress` configured — Core's own data isn't guaranteed to give
back an absolute URL for every field (confirmed live: exam 1061 has one
that isn't), and `HttpClient.GetByteArrayAsync` throws synchronously for a
non-absolute `Uri`, a different exception type than the `HttpRequestException`
already caught here for a failed *download*. Both builders validate the URL
is absolute (`Uri.TryCreate(src, UriKind.Absolute, ...)`) before fetching and
skip just that one image otherwise, rather than failing the entire export —
same "best effort over one bad input" spirit as the content-owner commission
accrual in `docs/business/content-delivery.md`.

`IHeadlessBrowserRenderProvider` is a singleton service — launching
Chromium per request is far too slow — with a `SemaphoreSlim` capping
concurrent render pages (formula renders and PDF prints share the same
limit) to `Environment.ProcessorCount`; a burst of simultaneous export
requests queues rather than piling unboundedly onto the one shared browser
process. See `docs/deployment/overview.md` for the native library
dependency this introduces.

**Word/PowerPoint page-level infrastructure**, built directly against the
OOXML tree (no HTML involved at all): explicit A4 `SectionProperties`/
`PageMargin` (Word: `Top=2977, Right=720, Bottom=720, Left=720, Header=720,
Footer=0` dxa — measured directly from the reference template's own real
`w:pgMar`, 2026-09-22, not guessed; the outsized top margin gives the
decorative header room to clear before body content starts, and `Footer=0`
lets the footer's own content sit right at the page's bottom margin with no
extra reserved distance). These live as `ExamWordDocumentBuilder`'s
`PageWidthDxa`/`PageMarginLeftDxa`/etc. constants — the one place page
geometry is defined — and `PageContentWidthDxa` (page width minus left/right
margins) is what `BuildHeaderRowAsync`'s table and `BuildHeaderBackgroundParagraph`'s
decorative shapes size/position themselves against, rather than each
hardcoding its own content-width number: an earlier revision had two
separate hardcoded copies that both silently went stale (still assuming the
*previous*, narrower margins) when the margins above were corrected, quietly
narrowing the header below the question tables' own real width until fixed
2026-09-22. `BuildHeaderBackgroundParagraph` also adds a plain, corner-less
filler rectangle (same `#F2F4F7` fill as its bottom bar) continuing from
where the reference's own transcribed background shapes end down to just
above the body's top margin — the header table's real content is much
shorter than that margin, so without it the page showed a large blank gap
before the first question. The header table's own brand row (logo/portrait/QR,
`BuildHeaderRowAsync`) is given an explicit `AtLeast` height (900dxa) so the
row has a bit more clearance around the wordmark image than its own natural
content height (~950-980dxa) would otherwise give it. Every Word `TableRow` marked `CantSplit` so a question can't
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


## Google Docs compatibility mode (Word export)

Google Docs' DOCX importer fails to open the whole file ("File could not open") when it meets Word's native
vector shapes (`wps:wsp`, even wrapped in `mc:AlternateContent`) - confirmed by diffing an export with and
without the header background shapes. `GET exams/export` therefore takes an optional `googleDocsCompatible`
flag (Word only, default `false`). When `true`, `ExamService` runs the finished document through
`GoogleDocsDocxSanitizer.StripUnsupportedShapes`, which removes `wps` shapes, `mc:AlternateContent` blocks that
carry them, and non-picture floating DrawingML from the body, headers and footers, plus any run/paragraph left
empty by that (never a table cell's last paragraph). Text, tables, cell formatting and pictures (inline or
floating) are untouched; a document with nothing to strip is returned as the same byte array. Without the flag
the export is unchanged (full header background). The VML watermark is not touched.
