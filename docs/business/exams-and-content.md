# Exams, Curriculum & Content

Business logic: `src/Application/Service/BoardService.cs`, `GradeService.cs`,
`SubjectService.cs`, `TopicService.cs`, `QuestionService.cs`,
`ExamSerivce.cs` (filename typo in the repo — "Serivce" not "Service"),
`GameSerivce.cs`, `PostService.cs`, `ExamImportService.cs` (+ `ExamImportRules.cs`,
`ExamImportText.cs`). Entities in `src/Domain/Entity/`:
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
composed of exam metadata (title, type, level, time limit, test count,
author) plus a list of "Tests" (individual question items with up to 4
options and their correct option) —
i.e. locally-authored `Question` entities are not the source for formal
exams; those live in the external system.

**Where the exam data comes from (changed 2026-09-24, bulk since 2026-10-01).**
`CoreProvider.GetExamInformationAsync` makes two read-only gama-api calls,
each with 3 attempts, since gama-api intermittently answers "Target resource
is no longer available":
1. `Core:Exam` (`exams/{id}`) returns the exam's details and its question
   ids in exam order (`tests`).
2. `Core:ExamTest` (`examTests?exam_id={examId}`) returns all of the exam's
   questions in full, including `true_answer` (1–4). The code puts them in
   `tests` order and matches them by id. (Until 2026-10-01 this was one
   `examTests?id={questionId}` call per question, up to 8 in parallel.)

If any id in `tests` is missing from that list, the whole export fails,
rather than producing an exam with missing questions. This replaced `exams/start/{id}`, which:
- never returned correct answers (so the Answer Key was always empty);
- returns a `startID`, i.e. appears to start an exam attempt for the
  exporting user as a side effect.

Things found on the way, so they aren't rediscovered:
- The *path* form `examTests/{id}` (the existing `Core:Test`, still used by
  the unrelated submission check) refuses some questions with
  "permissionDenied" (exam 1061's Q33, id 27839), while the query form
  returns them.
- The `?id=` query form ignored an id it didn't recognize and returned the
  whole question bank (~44k questions). By 2026-10-01 it ignored the id
  altogether, with or without a token, returning 10 sample questions
  (`"mode":"sample"`) -- which broke the per-question export and is why it
  moved to `exam_id`.
- `examTests?exam_id=` returned an empty list for every exam on 2026-09-24.
  Re-tested 2026-10-01 (exams 1000, 1061, 1200, without a token), it returns
  exactly the exam's questions, in `tests` order, unpaged up to at least 40,
  and an empty list for an unknown exam -- so the export switched to it. The
  parameter must be `exam_id`: `examId=`/`exam=` are ignored and return 10
  unrelated questions. The match against `tests` stays as the safety net.

Header fields from this data:
- **Level:** gama-api `level` 1/2/3 is shown as Easy/Medium/Hard (it
  replaced `exams/start`'s `score_type`). `0` (or nothing) means no level is set, e.g. an exam made by the MCP
  import: since 2026-10-09 it maps to `null` (`CoreProvider`), so Word/PDF show `Difficulty Level: —` and the
  PowerPoint title slide leaves the fact out, instead of printing `0`.
- **By:** the author. `exams/{id}`'s `user_id` is the author's gama-api user
  id, i.e. our `ApplicationUser.CoreId`. `ExamSerivce.ApplyLocalAuthorAsync`
  looks up that local user:
  - their first/last name replaces gama-api's;
  - their avatar (downloaded from the file CDN, center-cropped and masked to
    a circle, `ToCircularAvatar`) replaces the placeholder portrait in the
    Word and PDF headers.
  If there is no linked account, no name, no avatar, or the download fails,
  it keeps gama-api's name and the placeholder rather than failing the
  export. (`exams/{id}`'s separate `uid` field is the *requesting* user's
  gama-api id, not the author's.)

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
real content). Each question is: one header row (question number + text —
**no longer `w:vMerge`-spanned across two rows, fixed 2026-09-23**: see the
pagination note below for why), then its option rows, then two spacer rows
— the first carrying the navy separator as its own
`w:tcBorders` bottom border (color `SeparatorNavy` = `#002060`, `single`,
size 8 = 1pt — the reference's own real measured value, not the earlier
approximate brand navy), the second blank padding beneath it (skipped for
the exam's very last question). **Padding sizes corrected 2026-09-23**
(measured against `Temp.docx`: ~33px/4.2mm above each question's content and
~30-34px/3.7-4.3mm below it at 200dpi, vs. this export's previous ~13px/17px
— roughly half, since neither spacer row had ever been given deliberate
padding, only collapsed-near-zero height via the same technique used
elsewhere in this file for rows that should contribute ~nothing). Both rows
now use `BuildSpacerParagraph` at a real font size instead of the near-zero
one (`QuestionBottomPaddingFontSizeHalfPoints`/`QuestionTopPaddingFontSizeHalfPoints`,
tuned empirically against rendered output, not derived from a formula, since
row height from a blank paragraph's mark-run font size doesn't map to a
simple closed-form pixel/dxa conversion).

**A question never splits across a page break (fixed 2026-09-24).** Each
question is **one wrapper row** in the shared table: its single cell spans
the whole grid (zero cell margins) and holds a nested table built by the
same `BuildSharedQuestionTable` (identical grid), carrying the question's
real rows — number/text row, then option rows or a descriptive image row —
followed by a collapsed 1pt paragraph (a cell must end with a paragraph).
The two separator/padding rows stay directly in the shared table. That
wrapper row carries `w:cantSplit` (`PreventRowsSplittingAcrossPages`), so
the whole question moves to the next page as one unit in Word, LibreOffice
and Google Docs alike; only a question taller than an entire page still
splits (no alternative). The cost is the space a question leaves at the
bottom of a page when it doesn't fit (exam 1000 even got one page shorter,
12 → 11, from the tighter packing elsewhere).

History, so it isn't retried: with a question's rows sitting directly in the
shared table, `cantSplit` only stopped each *row* splitting, and the
`w:keepNext` chaining meant to glue a question's rows together is honored by
Word but ignored inside tables by LibreOffice — found live on exam 1061 Q7 and
exam 1000 Q4 (number/text row at the bottom of one page, its image/options on
the next). One-table-per-question (2026-09-23) behaved identically, since the
rows inside each table could still separate. Earlier still, a two-row
`w:vMerge` question-text cell was itself a page-break opportunity in
LibreOffice; it was replaced by one ordinary row that grows to fit.
`keepNext` is still set on the wrapper row's paragraphs, so Word additionally
keeps a question with its own separator line below it.

Every question's options are normalized onto one of four
`ExamWordDocumentBuilder.QuestionLayoutType` values (`TextHorizontal`,
`Text2x2`, `TextVertical`, `ImageOptionsHorizontal`), each with its own
dedicated `Build*OptionRowsAsync` method returning the row(s) to append,
dispatched from `BuildOptionRowsAsync`. `ClassifyLayout` picks the type per
question, **preferring Core's own real fields over guessing from content
shape** — confirmed by live-querying `GET exams/start/{id}` (then `Core:ExamInfo`, since replaced — see "Where the exam data comes from") for exams
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
assumed). `LoadSharedQuestionImageAsync` loads it once. For `Text2x2`/
`TextVertical`, the badge/option split shrinks to free the last
`ImageFineColumnSpan` (4) fine columns for it — merged via real `w:vMerge`
across however many option rows that layout has (`w:vMerge` across 2 rows
for `Text2x2`, across 4 for `TextVertical`), never four separately-placed
pictures or a picture floated beside the whole question. **`TextHorizontal`
is the exception, fixed 2026-09-23 for better UX**: its own shared image no
longer shrinks the option columns to share a row with them at all — it gets
a full-width row of its own, centered, directly above the options
(`BuildCenteredFullWidthImageRow`, the same helper `BuildDescriptiveImageRowAsync`
uses, shown at the image's own original size — only shrunk when wider than
`MaxImageWidthPx`, never enlarged). Found live on exam 1061's Q10: its own
shared image is a real 4-column comparison table (mass vs. weight,
options A-D), and squeezing that into the narrow ~1.6in side column made it
essentially unreadable even at full source resolution — the display width
itself, not just pixel density, was the real constraint. The first version
(2026-09-23) forced every such image to exactly `MaxImageWidthPx` wide,
which blew small sources up (Q13's 324px diagram took half a page, and
blurrier); switched to original size 2026-09-24. This costs some real page
count (exam 1061 grew from 9 to 12 pages), a deliberate, requested
trade-off of space for legibility. A descriptive question
(`!TestDto.HasOptions`) with its own `QuestionFile` has no options layout to
attach it to either; its image is simply centered in its own row via the
same shared helper. The option-number badge fill (`BadgeGray` =
`#EDEDED`) matches the reference's own real measured value (was an
approximate `#E7ECF2` before). Since 2026-09-26 the question-number badge is
the brand charcoal (`QuestionBadgeFill` = `#24292F`) with a white number
(`QuestionBadgeText`), in Word and Pdf, so question numbers stand out. The
badge is a small **nested 1×1 table** (`BuildNumberBadgeChip`), not shading
applied directly to the outer grid cell, centered (`w:jc` on the nested
table) inside the wider/unshaded outer badge cell. Since 2026-09-26 it's a
**square**: fixed width and exact row height (`QuestionBadgeSizeDxa` = 400,
20pt; `OptionBadgeSizeDxa` = 320, 16pt), fixed layout and zero cell margins
(Word's default 0.08in side margins, and the old auto-size with
`w:tcMar` padding, made it a rectangle), the number centered. The Pdf's
`.chip.q`/`.chip.o` use the same sizes. Option badge cells have 2pt above and below
(`OptionBadgeVerticalGapDxa`, 2026-09-27), so stacked options' badges (e.g.
exam 1061 Q12) keep a 4pt gap instead of touching. The number's paragraph has explicit
zero spacing: the export has no styles part, so Word 2019 falls back to 8pt
after every paragraph that doesn't set its own, which pushed the number up
inside the badge (LibreOffice's fallback is 0). This replaced an earlier run-level
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
redesign. **The squares are marked since 2026-09-24:**
`TestDto.CorrectOption` is filled from gama-api's per-question `true_answer`
(see "Where the exam data comes from" above), so the correct option's square
is filled (■) and the rest stay empty (□). This is shown to **everyone who
can export**, by product decision (2026-09-24): any logged-in user can
export any exam with its answers. Restricting it to the exam's
owner/admins/teachers (`exams/{id}` reports those flags for the requesting
user) was considered and not chosen.

**Descriptive answers (2026-09-25).** Descriptive questions have no correct
option (gama-api `true_answer` `"0"`), but gama-api's `answer_full` holds a
worked answer (rich text with `$...$` formulas) and `answer_full_file` an
optional image. These map to `TestDto.AnswerHtml`/`AnswerFile` and are shown
after the grid, one per descriptive question that has either (see
`ExamWordDocumentBuilder.DescriptiveAnswers`). Each is laid out like a
question: its question number in the same badge, the answer in regular 10pt,
the image below, and one unsplittable wrapper row with navy separators
(`AppendWrappedRowGroup`, shared with questions). Formulas go through the
same pipeline as question text: native equations in Word, MathJax in the
PDF.

The section's rules, identical in Word and PDF:
- The multiple-choice grid appears only if the exam has at least one
  question with options. An all-descriptive exam (e.g. 831) gets just the
  answers, not a grid of empty squares.
- A "Descriptive Answers" sub-heading is added only when both parts are
  present.
- No Answer Key page at all when there is neither.
- PowerPoint has no answer section.

Some of gama-api's stored answers contain leftovers of its editor's XSS
filter (e.g. exam 831 Q5: `... < 23 xss=removed>`). That is in their data
and shows the same way on the website.

**Answer Key measurements matched to Temp.docx (2026-09-23).** The reference's own answer-key
tables were measured directly from its `document.xml`/`styles.xml` (column widths, borders, fill,
font size, gap between blocks) and applied exactly:

- **Column widths.** The reference's own block columns vary slightly per block/row (Q# column
  545-571dxa, each option column 445-451dxa) — `AnswerKeyNumberColumnDxa` (550) and
  `AnswerKeyOptionColumnDxa` (447) use representative values from that range, not an average of the
  whole page.
- **Gap after every block, including the last (deliberate deviation from the reference,
  2026-09-23).** The reference only gaps the first 3 blocks in a row (364-366dxa measured) — its own
  last block sits flush against the page's right margin, since that's just where the leftover
  column-width math happened to land, not a deliberate design choice. Visually this read as the
  block being "stuck" to the page edge, so `AnswerKeyGapColumnDxa` instead gaps *every* block
  (including the last), solved algebraically so 4 blocks + 4 gaps fill `PageContentWidthDxa`
  exactly, landing at 278dxa — smaller than the reference's own ungapped-last-block math (371dxa)
  would give, but present on all four sides instead of three.
- **Borders are per-cell, not table-level.** The reference uses Word's `TableGrid` style
  (`single`/`auto` (renders black)/`sz=4`, i.e. 0.5pt) with per-cell `nil` overrides to draw only: a
  continuous outer box around each 10-question block, a divider between the question-number and
  option-A columns on every question row (but *not* the header row, whose merged yellow bar has no
  internal lines), and never a line between two option columns or between two question rows. Since
  this file uses no named table styles anywhere (`RemoveDefaultTableStyle` strips them repo-wide),
  that pattern is reproduced with explicit per-cell `AnswerKeyCellBorders`/`AnswerKeyColumnEdges`
  calls instead of a style + selective `nil`, rather than the table-level, uniform
  `BorderedTableBorders` used before this change (which put a light-gray line between *every*
  column, including between option columns and between the header and body, that the reference
  never has).
- **Fill colors and font size.** The header row's real fill is `#FFE599` (`AnswerKeyHeaderYellow`,
  corrected from an approximate `#FBE1A0`) and every run in the block (header numbers 1-4, question
  numbers, option marks) is `sz=24` (12pt), not the previous 9pt (`fontSizeHalfPoints: 18`).
- **Row height / cell padding.** The reference sets no explicit row height on any answer-key row
  (auto, driven by content) — matched by leaving these rows' height unset. Its `tblCellMar` is
  `left`/`right = 108dxa`, `top`/`bottom = 0`, reproduced with a per-cell `AnswerKeyCellMargin` (this
  file already sets per-cell `tcMar` rather than a table-level default elsewhere, e.g.
  `BuildNumberBadgeChip`, so the same pattern is used here for consistency).
- **Vertical gap between rows of blocks.** The reference has no explicit paragraph formatting on the
  blank paragraph between one row of blocks and the next — its real ~438dxa (~0.3in) gap comes
  entirely from the default "Normal" paragraph style (12pt font, `160dxa` after-spacing) defined in
  its own `styles.xml`. This export ships **no `styles.xml` part at all**, so an unformatted empty
  paragraph there would fall back to whatever default each renderer picks on its own (LibreOffice
  and Word disagree) instead of a value this file controls. `BuildAnswerKeyRowGapParagraph` sets the
  same 12pt run size and `160dxa` after-spacing explicitly so the gap is reproduced deterministically
  regardless of renderer.
- **Option marks stay Unicode, not the reference's Wingdings 2 symbols.** The reference draws its
  empty/filled squares via `<w:sym w:font="Wingdings 2" w:char="F0A3"/>` (a typeface-dependent glyph
  reference), not literal text. This export keeps its existing plain-Unicode `■`/`□` characters
  instead of switching to `w:sym` + Wingdings 2 — that font may not be installed wherever the
  document is opened/converted (Word has it bundled; LibreOffice, Google Docs, and Linux renderers
  are not guaranteed to), and the Unicode characters already render visually equivalent at the
  corrected 12pt size. This is a deliberate, known deviation from the reference's own technique, not
  an oversight.
- **"Answer Key" title spacing (2026-09-23, user visual feedback, not measured from the reference).**
  The title sat too close to the page's own running header above it, and too far from the first row
  of blocks below it. Fixed with explicit `w:spacing` on the heading paragraph itself
  (`Before="400"`, `After="80"`) instead of the surrounding blank paragraphs it previously relied on
  for spacing — more room above the title, much less between it and the tables.

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

**Pdf** renders formulas as images: `IHeadlessBrowserRenderProvider
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
   dependency. Four real bugs were found and patched in the vendored copy
   (see its header comment) by validating actual output against
   `DocumentFormat.OpenXml`'s `OpenXmlValidator` — "well-formed XML" and
   "schema-valid OOXML" are different checks, and only the latter reliably
   predicts whether Word/PowerPoint will show a repair prompt. The third
   (fixed 2026-09-24) was in `textContainer()`'s `mathvariant` branch
   (e.g. an upright `Ω`/`mi mathvariant="normal"`): it wrote `w:rPr` before
   `m:rPr`, put `m:nor` and `m:sty` together (the schema allows one or the
   other), and emitted `m:sty m:val="undefined"` for `normal` — 27
   validator errors on exam 1061 alone. It now writes `m:rPr` first with
   only `m:nor`, then `w:rPr` carrying bold/italic; Word and PowerPoint
   exports of exams 1061/831 validate at 0 errors. Also added the same day,
   for content authored with only the script inside the TeX delimiters
   (e.g. `37 ms$^{-1}$` in exam 1061 Q27, or `12$^\circ$`): before MathJax
   runs, `RenderToOmmlScript` (`HeadlessBrowserRenderProvider`) pulls the
   word glued to an opening `$` that starts with `^`/`_` into the formula as
   its base (`ms$^{-1}$` → `$\mathrm{ms}^{-1}$`), so the exponent belongs to
   "ms" instead of floating after it. "Word" means Latin (incl. accented, e.g.
   `Å`), Greek (`Ω$^2$`, `μ$_0$`), digits and the micro sign `µ`; RTL scripts
   (Persian/Arabic) are deliberately left out, since pulling them into an LTR
   formula would scramble their order. Any script slot still empty after that
   (nothing glued in front, e.g. `($^{-1}$)`) is filled with a zero-width
   space by the vendored converter — otherwise Word/LibreOffice draw a
   dotted placeholder box there.
3. **Word** (`ExamWordRichText`): the `<m:oMath>` fragment becomes a direct
   `OfficeMath` sibling of `w:r` runs within the paragraph — inline with
   surrounding text, same as Word's own equation editor. A paragraph holding
   *only* equations is a display equation to Word, which centers it (found in
   Office 2016 on exam 1061 Q2's fraction options, 2026-09-24; LibreOffice
   doesn't center, so its previews hid it), so `LeftAlignLoneEquation` wraps
   those in an `m:oMathPara` with `m:jc="left"`.
4. **PowerPoint** (`ExamPresentationBuilder.BuildRichParagraphs`): unlike
   Word, DrawingML's `a:p` has no slot for a bare `m:oMath` — PowerPoint
   2010+ represents slide equations via an `mc:AlternateContent`/`a14:m`
   markup-compatibility wrapper instead (`mc:Choice` requiring the `a14`
   extension, `mc:Fallback` a readable one-line text form for consumers
   without it). Since 2026-09-25 each formula stays inline in its sentence
   (the AlternateContent sits between the paragraph's `a:r` runs, which is
   how PowerPoint itself stores inline equations; it validates at 0
   errors), and a formula-only paragraph is one left-aligned display
   equation. The earlier claim that it couldn't be mixed with runs was
   wrong: that one-formula-per-line layout is what users saw as broken
   formulas, centered in Office 2016.
5. Both paths fall back to the same rendered-PNG `<img>` PDF uses, per
   formula, if the MathML→OMML conversion throws — one bad formula degrades
   to an image rather than failing the whole export. The fourth
   (fixed 2026-09-27): `parse()` trimmed text-container content with JS
   `trim()`, which also strips U+00A0, the non-breaking space MathJax emits
   for `\text{ }`, so a formula's only space vanished (exam 831 Q4's
   "4.20 millilitres²" came out "4.20millilitres²" in PowerPoint; Word hid
   it with its own math spacing). It now trims ASCII whitespace only.

**PowerPoint export restyled, with answer slides (2026-09-25).**
`ExamPresentationBuilder` was rewritten to match the Word/Pdf design on 16:9
slides.

Layout:
- **Title slide:** the brand panel on a light band, the QR code, the exam
  title, questions/time/level and the author.
- **Every other slide:** the brand panel (the sharp `exam-gama-wordmark.png`)
  plus the exam title in a header, and a footer with "question n / total"
  and the linked Gama logo + gamatrain.com.
- **Questions:** a grey number badge, then the options in the same
  arrangement as Word/Pdf (`W.ClassifyLayout`: 4 across, 2x2, stacked, or 4
  image options), each with a grey number badge. A shared question image
  sits right of 2x2/stacked options, or above 4-across options.

Fixed on the way:
- Pictures used to be resampled down to their display size (the blurry
  logo) and stretched into fixed boxes. They're now embedded at full
  resolution, shown at real size and only shrunk proportionally to fit.
- Question/option HTML is converted like Word's: paragraphs, bold, italic,
  underline, superscript/subscript and color are kept.
- Formulas were each split onto their own line as a centered display
  equation. They're now inline in their sentence, as PowerPoint equations
  (`mc:AlternateContent`/`a14:m` + OMML). Word's `w:rPr` inside math runs is
  replaced by DrawingML `a:rPr` (the sentence's size/color, Cambria Math),
  as PowerPoint stores it. A paragraph holding only formulas becomes one
  display equation with `m:jc="left"`, since Office centers it otherwise.
- The equation fallback for viewers without PowerPoint equations
  (LibreOffice Impress, likely Google Slides; they always show it) is
  formatted text (`FallbackRuns`), not the letters run together:
  - powers and indices are real superscript/subscript runs (`baseline`),
    so `m s⁻¹` and `10⁷` look right;
  - fractions are `(a)/(b)` and roots `√(...)`, since plain text can't
    stack them;
  - it uses the surrounding text's size, color and weight.

Answer slides:
- A question with an answer (a correct option, or a worked answer/image)
  gets a **hidden** answer slide right after it.
- The question slide's bottom-right "Show Answer ›" button jumps to it
  (`ppaction://hlinksldjump`), and its "‹ Back to Question" button jumps
  back.
- The answer slide is the same slide with an "ANSWER" tag. The correct
  option is highlighted in green; for a descriptive question, the worked
  answer (and image) replaces the options on a light green panel.
- Hidden (`show="0"`), so a normal slideshow still goes question to
  question.
- The slide layout carries the relationship back to its slide master that
  the format requires (`BuildSlideLayoutPart`). PowerPoint and LibreOffice
  tolerated it missing, but Google Slides refused the whole deck ("File
  could not open"). Found 2026-09-25 by importing variants through `rclone
  --drive-import-formats pptx`, which runs Google's importer and reports its
  400 error; handy for checking any change against Google Slides.
- The slide-to-slide links are written with relative targets
  (`Target="slide3.xml"`, as PowerPoint writes them;
  `MakeSlideLinksRelative`). The SDK's default absolute form
  (`/ppt/slides/slide3.xml`) made LibreOffice Impress try to open it as an
  external file instead of jumping to the slide.

Long content continues on extra slides at full size, never shrunk or cut
off. Each slide's content is a list of blocks (each question/answer
paragraph, a picture, each row of options, with estimated heights), and
`Paginate` fills a slide from the top and carries what doesn't fit onto a
"CONTINUED" slide. For the answer slides:
- A multiple-choice question's answer slides mirror the question slides
  that hold its options (same split, correct option highlighted), each with
  its own Show Answer / Back pair.
- A descriptive question's worked answer runs over as many hidden answer
  slides as it needs, reached from the last question slide and chained with
  "More ›"; "Back to Question" returns to that slide.
- A picture above 4-across options is sized to the room left after the
  question text and the options row (floor: 35% of the slide), so short
  options aren't pushed alone onto a continuation slide.

Exam 1061: 85 slides (title, 40 questions + 3 continuations, 41 answer
slides).

Equations and Office: PowerPoint showed the plain-text fallback instead of
the equations until the namespace declarations were placed exactly as
PowerPoint writes them: `xmlns:mc` on `mc:AlternateContent`, `xmlns:a14`
on `mc:Choice`, and `xmlns:m` on the math element. Declared only on the
enclosing `a:p`, Office didn't resolve `Requires="a14"` (found 2026-09-25).
- LibreOffice's PDF conversion skips hidden slides, and it can't draw
  PowerPoint equations (it shows the fallback text).

**Header metadata row (2026-09-25).** Name/School/Questions/Time/Level were
made 4 columns each in Word and Pdf. Since 2026-09-26 the School cell and
the "Date:" label/value cells (row 2, beside the title) are gone: the title
spans the full width, and Name/Questions/Time/Level are 5 columns each.
Since 2026-09-27 a difficulty gauge icon precedes the "Difficulty Level:"
label (Word and Pdf), picked by the exam's level: `exam-level-easy.svg`,
`-medium.svg`, `-hard.svg` (the designer's SVGs, `viewBox` added so they
scale), shown at 20x10px (24x12 at first; shrunk to match the other icons); Word uses a 128x64 PNG render of each. A level
other than Easy/Medium/Hard gets no icon. To make room, the row is now
Name 6 / Questions 4 / Time 4 / Difficulty Level 6 columns
(`MetadataNameColumns` etc.). "Name:", "Questions:" and "Time:" get icons too: Material Symbols
`id_card`, `contact_support` and `timer` (supplied by design), as
`exam-icon-name.svg`/`exam-icon-questions.svg`/`exam-icon-time.svg` in
the gauge's dark grey (`#344054`), 14px. Word's PNGs of these three carry
transparent space above the icon (96x123, shown 14x18px) so that, centered
on the line, the icon sits level with the text; LibreOffice ignores
`w:position` on inline pictures. The Pdf sets them `vertical-align:-2px`.
The Pdf sets the gauge `vertical-align:0.5px` so its top lines up with
theirs; in Word, centered on the line, it already does.
`HeaderBrandAssets` holds all label icons as `HeaderIcon(Png, Svg)`.
The Level cell's label reads "Difficulty Level:" (also on the PowerPoint
title slide); "Difficulty Level: Medium" nearly fills the Word cell, and
the row height is fixed, so a longer level value could wrap and be cut off.

**Header Topics row (2026-09-26).** When gama-api's `exams/{id}` has
topics, a fourth header row (Word and Pdf, every page) shows "Topics:" and
their titles (trimmed, in syllabus `order`, comma-separated) across the full
width. `topics` comes in two shapes, handled by `CoreExamTopicsConverter`:
an array of `{id, order, title, ...}` (exams 2050, 2037, 831), or a string,
`""` when there are none (exam 1061; no row then). A non-empty string is
taken as one title, unless it's only ids. The exam-level list isn't always
what the questions cover (2050 lists four units, all its questions are from
one of them); the per-question `topics_title` from `examTests` isn't read.
The row is 1-3 lines tall (estimated at ~95 characters per line; a longer
list is cut with an ellipsis) and everything under it grows to fit:
the header background's bottom bands and Header Outline (every point below
y=80 in the 547x104 space moves down, `HeaderBackgroundShapesFor`) and the
page's top margin (`PageMarginTopFor`, also used by the Pdf and thumbnail).
PowerPoint shows the same text on its title slide, under the
Questions/Time/Difficulty Level line (16pt); when present, the title block
moves up (by at most 450000 EMU) to keep it clear of the footer.

**PowerPoint, 2026-09-27.** Matched to the Word/Pdf design: question badges
are charcoal with a white number (`QuestionBadgeFill`/`QuestionBadgeText`),
option badges grey squares; the title slide's Questions/Time/Difficulty
Level line leads each fact with the same icon as the Word/Pdf header
(`AppendFacts`, one picture + text box per fact); the header title on every
other slide is grey (`TextMuted`). All text paragraphs have 120% line
spacing. `EstimateTextHeight` (which places each paragraph below the last)
now assumes 1.44x the font size per line and ~0.55em per character, and
counts a formula's own characters (its OMML `m:t` text) -- before, a
paragraph with a formula (exam 831 Q4) wrapped a line more than estimated
and the next paragraph was drawn over it. Badge and button labels stay at single spacing: 120%
adds its extra space above the text, which pushed the number below the
badge's center.

**Sub-part indentation, 2026-09-27.** In a question's text and in a
descriptive answer (not in options), a paragraph that starts with a part
label -- `a)`, `(a)`, `a.` (a-h) -- or a sub-part label -- `(i)`, `i)` (i-x),
followed by whitespace -- is indented (`ExamWordRichText.SubPartLevel`):
0.25in (parts) or 0.5in (sub-parts) past the text's left edge, with a
hanging indent about the label's width (0.21in / 0.31in) so a wrapped line
lines up under the text after the label. Paragraphs between parts (shared
context, e.g. exam 831 Q4's "A random sample of 50 bottles...") stay put.
Word: `w:ind` left/hanging (set through the typed `ParagraphProperties.
Indentation`, which keeps `w:pPr`'s schema order; `ZeroSpacingParagraphProperties`
now uses the typed `SpacingBetweenLines` setter for the same reason). Pdf:
`padding-left` plus a negative `text-indent`. PowerPoint: `a:pPr`
`marL`/`indent`, scaled by font size from the Word values, and the height
estimate uses the narrower width. The reference's narrow empty spacer
column was dropped, because "Level: Medium" didn't fit the 3 columns Level
had and its wrapped line was cut off by the fixed row height (LibreOffice's
red overflow marker).

**Thumbnail export (2026-09-25).** `fileType=Thumbnail` (`ExportFileType.Thumbnail`,
value 3, `.webp`, served as `image/webp`; the document formats stay
`application/octet-stream`) returns a 496x792 WebP picture of the Pdf
export's first page. How it's made:
1. The PDF is built and printed as usual, only to read its real page count
   (`/Type /Page` objects) for the footer's "1 / N".
2. `ExamPdfHtmlBuilder.BuildThumbnailDocument` lays that first page out as
   its own single-page document: the same header/footer templates and
   formula-rendered body, the PDF's margins, and Chromium's template padding
   recreated. A small script hides the first question that doesn't fit and
   everything after it, reproducing the PDF's first page break.
3. `IHeadlessBrowserRenderProvider.RenderScreenshotAsync` screenshots it at
   1.5x.
4. `ExamSerivce.ToThumbnailWebp` scales it to 792px tall and crops the
   middle 496px. A4 is wider than 496:792, and at that scale the trim (~32px
   a side) is only the page's blank side margin.

No PDF-rasterizing library is involved: the only ones available were paid
(Spire, removed earlier) or untrusted.

**Sharp logo (2026-09-25).** The header's brand panel used to be a 540x104
PNG (only 2x its 270x52 display size, ~190dpi on paper), so it looked soft
in every export. It's now built from the frontend's vector
`gamatrain-logo.svg` (the white Gama wordmark), drawn on the header's own
dark panel shape (`HeaderBackgroundShapes`' Shape 2, square bottom-left):
- `wwwroot/exam-gama-wordmark.svg` is vector; the Pdf embeds it as SVG.
- `wwwroot/exam-gama-wordmark.png` is that SVG rendered at 4x (2160x416),
  for Word, whose SVG support is uneven across viewers (older Office,
  LibreOffice, Google Docs).

To change the logo, edit the SVG and re-render the PNG from it, with
headless Chrome at `--force-device-scale-factor=4 --window-size=540,104`
and a transparent background.

**Pdf matches the Word export's design (2026-09-24).** It used to build from
a separate, older `exam.word.html` Handlebars template with its own look
(dark "gamatrain" banner, boxed questions, A/B/C/D letters, no answer key).
That template is gone: `ExamPdfHtmlBuilder` now lays the PDF out exactly
like `ExamWordDocumentBuilder` (same page geometry, header, question grid,
number badges, navy separators, answer key, footer and watermark), and
`IHeadlessBrowserRenderProvider.RenderPdfAsync` prints it with Chromium's
own print engine, as before. To keep the two from drifting, the PDF builder
reuses the Word builder's own pieces rather than copies: its constants
(page margins, column widths, colors, badge padding, image caps, answer-key
measurements), `GetOptionModels`/`ClassifyLayout` (which options layout a
question gets) and `HeaderBackgroundShapes` (the header background's path
data, drawn as DrawingML in Word and as inline SVG in the PDF). A change to
*how* something is drawn still has to be made in both builders.

Converting the Word file to PDF (LibreOffice on the server) was considered
and rejected: ~8s per export vs ~1–3s, and LibreOffice can't be installed on
the production Azure Web App with a plain code deploy.

How the PDF reproduces Word:
- **Header/footer** are Chromium page templates (repeat on every page), built
  with inline styles and data-URI images, since templates can't use page CSS
  or load files. Chromium pads its template containers (nominally 0.4cm,
  measured ~14.8pt), which the templates shift back by
  (`ChromiumTemplatePadding`). The header also starts 1pt below the header
  distance, matching Word's pinned anchor line. Page margins are passed to
  `RenderPdfAsync` in inches (PuppeteerSharp rejects `pt`).
- **Question text** goes through the same reduction `ExamWordRichText`
  applies in Word: each top-level `<p>`/`<div>` is one paragraph, and only
  line breaks, images, bold/italic/underline, super/subscript and a CSS text
  color survive (`NormalizeRichTextAsync`). This also means no raw gama-api
  markup (scripts, inline styles, attributes) reaches the PDF page.
- **Each question** (its rows plus its navy separator) is one
  `break-inside: avoid` block, so it never splits across pages, same as
  Word's single wrapper row. The padding after the separator sits outside
  that block, like Word's separate padding row, so pagination matches.
- **Formulas** are still MathJax images (`RenderFormulasAsync`), since a PDF
  can't hold Word equations. Two changes bring them close to Word's: each
  formula is typeset with `\displaystyle` (Word shows inline fractions
  full-size), and each image keeps MathJax's own baseline offset instead of
  `vertical-align: middle` (which made every line holding a formula taller).
  Formula heights can still differ slightly from Word's equation engine, so a
  long exam can occasionally break a page one question earlier or later.
- **Images** use the same size rules as Word (original size capped at 500px,
  150px shared side images, image options at original size capped at ~108px). One that fails to load is
  hidden, like Word leaving out an image it can't download.
- **Font**: the Word export sets no font, so Word and Google Docs show their
  default, Times New Roman; the PDF asks for `Times New Roman` with
  `Liberation Serif` (metric-compatible) as the fallback for servers. (A
  LibreOffice preview of the .docx may substitute a different serif, such as
  Noto Serif, which looks larger. Compare against Times New Roman.)

Verified 2026-09-24 against LibreOffice renders of the Word export forced to
Times New Roman, at 200dpi: header, footer and answer key within 1–2px, and
question heights identical except where formulas are. Same page count on
exams 1061 (10), 1000 (11), 831 (3) and 2037 (2).

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

gama-api sends `"0"` rather than null in `q_file`/`a_file`...`d_file` for "no
image" (exam 1061: 24 of 40 questions). `CoreProvider.FileUrlOrNull` maps it
to null (fixed 2026-09-24), so no export treats it as an image. Before this,
the Word export's TextHorizontal layout added an empty full-width image row
for it, an unexplained blank gap under the question text.

**Image options show at their original size (fixed 2026-09-24).** In the
all-images options row (`ImageOptionsHorizontal`), each option image used to
be forced to exactly 90px wide, so small diagrams were enlarged (up to ~20%)
and wide ones squeezed. Exam 1061 Q14's four options (85/75/74/128px wide)
ended up at four different scales. They now keep their original size and
are only shrunk when wider than their cell (`MaxImageOptionWidthPx`: 3 fine
columns minus Word's default cell padding, ~108px), via
`EmbedImageFromSourceAsync`'s new `maxWidthPx` cap. The PDF export follows
the same rule.

**Embedded images always keep their source's native resolution (fixed 2026-09-23).** `BuildImageGraphic`
(shared by every image path in `ExamWordDocumentBuilder`/`ExamPresentationBuilder` — question/option
images, the header logo/QR/portrait, footer icons) used to resample the decoded bitmap down to its
*displayed* pixel size before encoding into the `.docx`/`.pptx`, on the theory that embedding a photo's
full original pixel data when it only shows at, say, 52×52 (a header logo) needlessly bloats the file.
That reasoning doesn't hold for a small *display* size backed by a small *source* image, which is
common for these shared-question-image layouts: found live on exam 1061's Q10 (`ImageOptionsHorizontal`
layout's shared answers table), whose real source (`https://core.gamatrain.com/uploads/azmoonImages/
H4FNXPZY0FQhxdBSJHDN.png`) is a crisp 657×154 PNG, but the export was resampling it down to 150×35 to
match its ~1.6in display width in the merged options column (`MaxQuestionSideImageWidthPx`, chosen
against a 96dpi CSS-pixel assumption baked into `EmuPerPixel`) — a quarter of its real resolution,
before Word/LibreOffice then upscaled that already-destroyed bitmap back up to fill the display box,
compounding the blur. `widthPx`/`heightPx` still cap the *displayed* size in the document (via
`widthEmu`/`heightEmu`) exactly as before; only the encoding step changed, to always feed the encoder
the original, unresampled bitmap. Word scales a picture's embedded bitmap to whatever `w:extent` the
drawing declares regardless of the bitmap's own pixel dimensions, so this costs some `.docx` file size
(exam 1061 went from ~121KB to ~364KB) but never costs sharpness at any zoom or print level — the
explicit trade-off the user asked for ("keep original image size always").

`IHeadlessBrowserRenderProvider` is a singleton service — launching
Chromium per request is far too slow — with a `SemaphoreSlim` capping
concurrent render pages (formula renders and PDF prints share the same
limit) to `Environment.ProcessorCount`; a burst of simultaneous export
requests queues rather than piling unboundedly onto the one shared browser
process. See `docs/deployment/overview.md` for the native library
dependency this introduces.

**Word/PowerPoint page-level infrastructure**, built directly against the
OOXML tree (no HTML involved at all): explicit A4 `SectionProperties`/
`PageMargin` (Word: `Top=2977, Right=720, Bottom=600, Left=720, Header=720,
Footer=0` dxa — measured directly from the reference template's own real
`w:pgMar`, 2026-09-22, not guessed, except `Bottom`, cut from 720 (0.5in) to
600 (30pt) on 2026-09-26 to fit the footer exactly; the outsized top margin gives the
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
2026-09-22. `BuildHeaderBackgroundParagraph` used to also add a fifth, plain
corner-less filler rectangle continuing from where the reference's own
transcribed background shapes end down to just above the body's top margin
(closing a blank gap before the first question) — **removed 2026-09-23 per
request**; no replacement gap-filler was added back, since the header
table's own content already reaches close enough to it that the gap wasn't
actually needed. The header table's own brand row (logo/portrait/QR,
`BuildHeaderRowAsync`) is given an explicit `AtLeast` height (900dxa) so the
row has a bit more clearance around the wordmark image than its own natural
content height (~950-980dxa) would otherwise give it. Every Word `TableRow` marked `CantSplit` so a question can't
be separated from its own answer choices across a page break; a native
`HeaderPart`/`FooterPart` with a real `PAGE`/`NUMPAGES` `SimpleField` (Word
recalculates these itself as it paginates — not hardcoded page-count text);
an optional watermark rendered as a VML `v:textpath` shape folded into the
same header part (a section can only have one default header, so it can't
be a second one).

**Header brand row and background shapes, tuned 2026-09-23.** Naming
convention for the header's decorative background shapes, used throughout
this file's own comments: **Base Band** (the off-white rounded rectangle
behind everything), **Black Panel** (the dark charcoal panel behind the
logo), **Gray Diagonal Panel** (the light-gray panel to its right, behind
the portrait/By:/QR area), **Gray Bottom Bar** (the bottom strip). Real,
requested changes, per real measurements against `Temp.docx`'s own render:

- The Brand Row's 4 cells (logo/portrait/By:/QR) had their top/left/right
  borders hidden (`BorderedGridSpanCell`'s `topBorder`/`leftBorder`/
  `rightBorder` parameters), leaving only each cell's bottom border — reads
  as one seamless bar instead of a boxed grid.
- Black Panel's bottom-left corner squared off (was rounded, matching the
  reference); Gray Diagonal Panel's diagonal-cut left edge is kept at the
  reference's own coordinates (a left extension closing the small gap
  against the Black Panel was tried and reverted on request), while its
  bottom-right corner is squared off and its right edge extended from x=491
  to the shape's own full x=547 width, closing a second, separate real gap
  that left the QR code sitting on plain white instead of the panel's own
  gray (also present in the reference).
- The QR code's own background color is now generated to match the Gray
  Diagonal Panel's fill (`#F2F4F7`) instead of the `SkiaSharp.QrCode`
  library's plain-white default (`CoreProvider.GetExamInformationAsync`,
  switched from the library's static `GetPngBytes` to its fluent
  `QRCodeImageBuilder(...).WithColors(...)` builder) — keep this in sync if
  that shape's fill color ever changes. Its cell is right-justified with a
  small ~10px (150dxa, same 96dpi-px convention `EmuPerPixel`/`dxaToEmu` use
  elsewhere) right margin instead of centered with the library's own default
  white quiet-zone as the only spacing.
- **Header table height matches the background (2026-09-24).** The
  background is a fixed 547×104-unit drawing scaled to the content width
  (≈1989dxa tall), but the table's height used to depend on its content,
  so it ended ~1.5mm short and the background's rounded bottom stuck out
  below it. The rows are now sized from the background's own constants
  (`HeaderBackgroundHeightDxa` etc.): brand row exactly the panel band's
  height (y:0-48, so its bottom border also lines up with the panels'
  bottom edge), metadata row exactly 320dxa, and the title row `AtLeast`
  whatever remains (minus a 15dxa allowance for the border lines). A one-
  or two-line title ends flush with the background; only an unusually
  long 3-line title grows past it. The background's anchor paragraph is
  also pinned to an exact 1pt line, with the shapes moved down by that
  1pt, since the unpinned line pushed the table ~25dxa below the shapes.
- **Rounded bottom corners (2026-09-24).** A Word table's corners can't be
  rounded, so the header table's outer left/right/bottom borders are hidden
  and a fifth background shape, the **Header Outline** (unfilled, 0.5pt,
  `BorderLightGray`), draws them instead: down both sides from the brand
  row's bottom edge and around the same 8-unit rounded corners as the Base
  Band/Gray Bottom Bar. `BuildAsync` takes `googleDocsCompatible`: that
  export's shapes are stripped afterwards, so there the table keeps its own
  square outer borders (`BuildHeaderRowAsync(outerBordersFromBackground:
  false)`). Known trade-off: the *normal* export opened directly in Google
  Docs (which skips all wps shapes) shows the header without outer
  left/right/bottom lines, just like it already drops the whole
  background. A "white mask in front of square borders" alternative was
  tried and dropped: LibreOffice paints table borders over every header
  shape, even ones in front of text.

**Footer layout (2026-09-26).** The footer fits the 30pt bottom margin
(`PageMarginBottomDxa` = 600) in two fixed-height lines, in Word and Pdf:
the logo + `gamatrain.com` link centered on a 12pt line
(`FooterLinkLineDxa`, `BuildFooterLinkParagraph`), then the wave shape at
the page's very bottom (15pt, `FooterWaveLineDxa`) with "page / pages"
centered on it (`BuildFooterWaveParagraph`) -- the page numbers used to sit
at the left of the link line. Both Word paragraphs have exact line heights
and zero spacing, so Word 2019's 8pt-after default (no styles part) can't
grow the footer. In Word the wave is a floating picture behind the text
(`wp:anchor`, centered on the margins, pinned to the page's bottom 15pt);
the Google-Docs-compatible export keeps it (only non-picture drawings are
stripped). The page numbers are complex fields (`AppendPageField`:
begin/instruction/separate/result/end runs, each 8pt): as `w:fldSimple`,
LibreOffice drew them at the paragraph's default size.

**Footer website link (`BuildFooterLinkParagraph`).** Since 2026-09-26
the icon is the Gama "G" logo (the frontend's favicon), replacing the
reference template's globe: the Pdf embeds it as vector
(`exam-footer-logo.svg`), Word/PowerPoint embed a 128px render of it
(`exam-footer-logo.png`), and the link reads `gamatrain.com` (no `www`).
Unlike the reference, whose
own "www.gamatrain.com" is plain, unlinked text, both the icon and the text
are wrapped in one real `w:hyperlink` (`footerPart.AddHyperlinkRelationship`,
an external relationship to `GamatrainWebsiteUrl` = `https://gamatrain.com`)
so the footer is actually clickable in the exported document. Visual style
is left as-is (brand dark, bold, no underline) rather than switching to
Word's default blue/underlined "Hyperlink" character style, since neither
the reference nor the rest of this export uses that look. Vertical
alignment (2026-09-24): an inline picture sits on the text baseline, so the
14px icon rose ~2pt above the 8pt URL. The paragraph now uses
`w:textAlignment="center"` and the URL run is raised a further 1.5pt
(`w:position="3"`). `w:position` on the picture run itself was tried first,
but LibreOffice ignores it on inline drawings, so it goes on the text run.

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

## Posts

`PostService.cs` (contract `IPostService.cs`) manages `Post`,
`PostComment`, `PostTag`, `Tag`. Since 2026-09-21 post moderation is **a status
on the entity itself, not the generic `Contribution` workflow**: `Post` and
`PostComment` carry `Status` (the shared `Status` smart enum: `Draft`, `Review`,
`Confirmed`, `Rejected`) and a `RejectionComment`. There is one row per post —
no second, unpublished "contribution" copy.

- **Create/edit** (`ManagePostAsync`): a user's post starts `Draft`
  (`Draft=true`) or `Review`, and is auto-confirmed if the user holds
  `SystemClaim.AutoConfirmPost` or the `AutoConfirmPosts` setting is on. **A
  user's edit of their own post always resets it to `Draft`/`Review` and clears
  the rejection comment — including a post that was `Confirmed`, which
  therefore drops off the public API until re-approved.** An admin's edit
  (`IsAdmin`) never changes the status. Slug uniqueness is checked across all
  statuses. Replaced image/podcast files are deleted after the save.
- **Moderation** (`ConfirmPostAsync` / `RejectPostAsync`, and the `...Comment...`
  pair): each transition is one atomic `UPDATE ... WHERE Status = <expected>`, so a
  double click can't double-award points. Confirm is allowed from `Review` **or
  `Rejected`** (an admin can re-approve); reject is allowed from `Review` **or
  `Confirmed`** (an admin can pull a published post back — it disappears from the
  public API, and the reason is shown to the author). Points are awarded only on
  `Review` → `Confirmed`: re-approving a rejected post awards nothing, and
  rejecting a confirmed one does not claw points back (same as the old flow, which
  never reversed post rewards). Confirm awards the contributor the `PostContributionPoints` /
  `PostCommentContributionPoints` setting as a `SuccessfulContribution`
  `Transaction` (`IdentifierId` = the post/comment id — previously the
  contribution id) and, when notifying, sends the existing confirmation e-mail
  template. Reject stores the admin's comment in `RejectionComment`.
- **Visibility**: everything public (post list/detail, next/previous, sitemap,
  like/dislike, comment listing, commenting) requires `Status = Confirmed`
  (`PublishedPostSpecification` adds the publish-date check for posts). Owners
  see their own posts of every status via `posts/mine`; admins see all via
  `admin/posts`.
- **Comments**: a comment is `Review` until approved. `PostComments` stays unique
  per `(user, post)`, so a `Rejected` comment is resubmitted **in place** (text
  replaced, back to `Review`); a `Confirmed` or pending one blocks a new
  submission. Commenting requires a published post. Captcha is enforced at the
  controller layer (`IGlobalService.VerifyCaptchaAsync`), not in `PostService`.

**Migration `AddStatusToPostAndPostComment`.** Existing rows default to
`Confirmed`. Its `Up` also folds pending post Contributions into the new
model without deleting anything (Contribution rows are left untouched as a
historical record): `Post` (4) contributions not yet linked to a live post
(`IdentifierId IS NULL`) in `Draft`/`Review`/`Rejected` become `Posts` rows with
the same status (tags and localized values copied; duplicate/missing slugs get a
`-{contributionId}` suffix or `contribution-{id}`), and the newest pending/
rejected comment contribution per user+post becomes a `PostComments` row unless
that user already has a comment there. **Not migrated:** contributions whose `Data` is not a well-formed JSON object (skipped, since `JSON_VALUE`/`OPENJSON` throw on it — see the sandbox outage below), and pending *edits* of an
already-live post (contribution with `IdentifierId` set) and `Deleted`
contributions — a post now has a single row, so there is nowhere to hold an
unpublished second version; those stay only in `Contributions`. Validated
against a throw-away SQL Server 2017 with seeded contributions.

Entities: `Post` (`Slug`, `Title`, `Body`, `ImageId`,
`PodcastId`, `LikeCount`/`DislikeCount`, `VisibilityType`, `Keywords`,
`ViewCount`, `Status`, `RejectionComment`); `PostComment` (one comment per user
per post, enforced by a unique index; carries `Status`/`RejectionComment`); `PostTag`
(`PostTag.cs:14-33`, join entity, unique on `(PostId, TagId)`); `Tag`
(`Tag.cs:16-42`: `Name`, `TagType`, unique on `(TagType, Name)`).

`TagType` (`src/Domain/Enumeration/TagType.cs:6-24`): `School`, `Post`,
`Feature` — scopes what a tag can be attached to. `CategoryType`
(`src/Domain/Enumeration/CategoryType.cs:54-85`) is the broader vocabulary
used by the Contribution system for schools and — still, for the point-reward
lookup and `Reaction` — post content (`School`, `SchoolComment`, `SchoolImage`,
`Post`, `SchoolIssues`, `RemoveSchoolImage`, `PostComment`), each carrying an
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
the export is unchanged (full header background). The VML watermark is not touched. With the flag, the builder
also keeps the header table's own square outer borders (the rounded Header Outline shape would be stripped), and
the sanitizer drops `wps` from the root's `mc:Ignorable` along with its namespace declaration (fixed 2026-09-24:
it used to leave a dangling `Ignorable="wps"`, one OpenXmlValidator error per sanitized header).

## Paid export (2026-09-27)

`GET exams/export` charges for Pdf, Word and PowerPoint (the thumbnail stays free):

- **Price** (`ExamExportPricing`, the one place the rule lives): the exam's question count
  (its `exams/{id}` question ids) x the format's multiplier, rounded up. Multipliers are
  admin settings -- `ExamExportPdfMultiplier`, `ExamExportWordMultiplier`,
  `ExamExportPowerPointMultiplier` in `ApplicationSettings`, defaults 1 / 2 / 2.5, so a
  40-question exam costs 40 / 80 / 100. They're nullable: an admin settings save that omits
  them keeps the stored values, and never-set means the default.
- **Charge**: `GameService.SpendPointsAsync` with `ContentType.Exam`, i.e. the `ExamDownload`
  subscription feature (alone or in a feature group), quota first, then points -- the same
  path as `POST downloads`. No content-owner commission: the export is our own generated
  product, priced by size, not the author's price.
- **Once per user + exam + format**: exporting the same exam in the same format again is free.
  Since 2026-09-27 this goes through the generic pay-once mechanism (see "Pay-once content"
  below): a `ContentPurchase` with `ContentType = ExamExport`, `ContentId` = the exam,
  `Variant` = the format. The charge happens before the (slow) generation and is refunded if
  generation fails; the purchase row is written only once the file exists. Two concurrent
  first exports can both be charged -- the unique index rejects the second purchase row and
  that request refunds its own charge.
- **Responses**: the file with `X-Export-Points` / `X-Export-Paid-By` /
  `X-Export-Already-Purchased` headers (CORS-exposed with `Content-Disposition`), or, when the
  charge is refused, the `POST downloads` JSON shape (`reason`, current plan, upgrade
  suggestions), so the frontend's insufficient-balance/upgrade handling is shared.
- **Prices up front**: `GET exams/export/prices?id=` returns each format's price and whether the
  caller already bought it, from one `exams/{id}` call (no questions loaded).

## Pay-once content (`ContentPurchases`, 2026-09-27)

Anything a user pays for once and then owns is one row in `ContentPurchases`
(`ContentPurchase`): `UserId`, `ContentType` (`PurchasableContentType`), `ContentId`,
`Variant` (a form of the content, e.g. an exam export's format; empty when it has one form),
`Points`, `PaidBy`, `CreationDate`, unique on (`UserId`, `ContentType`, `ContentId`,
`Variant`). It replaced the exam-only `ExamExportPurchases` (migration
`GeneralizeContentPurchases`, which keeps the existing rows) so a new kind of paid content --
e.g. a premium `Post` -- is a new `PurchasableContentType` member and a caller of
`IContentPurchaseService`, not a new table.

`IContentPurchaseService` holds the mechanics; each caller keeps its own pricing rule and
passes the price in:
1. `ChargeAsync` -- free (nothing charged) if the price is 0 or the user already owns it;
   otherwise `GameService.SpendPointsAsync` with the caller's spend `ContentType` (which picks
   the subscription feature, e.g. `Exam` -> `ExamDownload`): quota first, then points. A
   refused charge returns why, plus the current plan and upgrade suggestions.
2. The caller delivers the content, then `CompletePurchaseAsync` records it -- or `RefundAsync`
   if delivery failed. A concurrent duplicate purchase fails its insert on the unique index and
   refunds its own charge.

`GetPurchasedVariantsAsync` lists what a user owns of one piece of content (the exam export's
prices endpoint uses it for `purchased`).

## Exam import through the MCP connector (2026-10-08)

A teacher turns a past paper (PDF or Word) and its mark scheme into questions and a draft exam on
gama-api by talking to an AI assistant (ChatGPT, Claude, Codex...) connected to this API's MCP server
at `/mcp`. Gamatrain's staff can also start from a paper already on gamatrain (see "From a paper on
gamatrain" below). It replaced the separate Python service `gamatrain-exam-tools`. Sign-in: see
`docs/api/authentication.md`, "MCP connector (OAuth)"; tools and endpoints: `docs/api/endpoints.md`.

**Who does what.** The AI reads the paper and the mark scheme (it converts a Word file itself),
extracts the questions and answers, and cuts the figures out of the pages. **This backend never reads
or converts the paper**: it checks what the AI hands over against gama-api's rules and saves it
straight into a draft exam on gama-api, within the tool call. The AI's instructions (how the conversation
runs, its own steps and the extraction rules) are served by the `get_import_guide` tool from
`Presentation/Mcp/ExamImportGuide.md`.

**Guided prompts ("gamatrain exams", 2026-10-10).** The connector runs the conversation, the same way in
ChatGPT, Claude (web), Codex and Claude Code. The user types **gamatrain exams** (in Claude Code also the MCP
prompt `exams`, a slash command), the AI calls `open_exams`, and every flow tool's answer ends with the next
step: an `ask` (a question, its options in order and at most one typed field, each option with what picking it
does: a tool call with its arguments, the AI's own work, or a follow-up question) or a `next` alone (the AI's
own work: reading the paper, saving the questions, fixing one). The AI shows an ask word for word, with its own
picker when it has one (Claude Code's selectable prompt) or as a numbered list, and never makes up options;
the steps and their wording live in one place, `Presentation/Mcp/ExamImportFlow.cs`. The flow:
- **Home** (`open_exams`): continue my draft (only when there is one) / new exam from my own file / new exam
  from the Gamatrain directory (staff only) / my exams / sign out. Every path starts and ends there.
- **Gamatrain directory** (staff, `search_papers`): a field for a paper ID or words of its title, or the latest
  papers (`list_recent_papers`), then the papers to pick one, 15 a page. A number opens that paper directly
  (`load_paper`; words are searched when no paper has that id). Words go to gamatrain's own paper search
  (`GET search?type=test&is_paper=1&directory=1`, the directory's confirmed papers): it matches one piece of
  the title, so it gets the longest word that isn't a word of a paper type (gama-api's `exam_type` titles:
  Paper 1..6, Topical..., a paper's classification, not in its title; a query of paper-type words only is
  refused as too broad, `searchTooBroad`, and the field asked again) (and a year as `edu_year`: the last number from 1990 to 2100, as a
  syllabus code such as 2058 comes first, and only when another word is left for the title; when nothing is found
  with it, it is searched again without the year, as it may be a syllabus code), up to 300 papers newest first, and every
  word is then matched here against the title, classification, session and year, in any order (a number
  exactly, another word as a prefix): `9709 paper 1 2024` finds `Mathematics 9709/12` of May/June 2024, a
  Paper 1, also when its title has no year.
- **My own file**: attach the question paper and the mark scheme, then continue (with or without a mark
  scheme); the AI reads the cover, finds the ids and the past paper.
- **Exam details** (`set_exam_details`, `confirmed=false`): the card with every detail filled in, then create
  the draft (`confirmed=true`) or change a detail: board, grade, subject, or paper/session/year/duration. A
  changed detail is left out (with the ones under it), and the connector asks for it from gama-api's list.
- The AI reads the paper and saves the questions (progress lines only), then calls **`open_review`**.
- **Review** (`open_review(examId, flagged)`): how the draft stands (`36 saved · 3 need a look · 1 not saved`),
  then one question that needs a decision at a time: fix it (the AI) / keep it / drop it / let the AI write
  the missing answers (only when some lack one) / go to preview. Nothing is kept here between calls, so the AI
  passes back the `save_questions` rows that weren't simply saved, and each option's `next` carries the rest
  of the list. Questions whose only flag is an AI-written answer (expected without a mark scheme) are
  counted, not asked about one by one.
- **Preview** (`show_preview`): publish (asks once more) / change a question (its number in the preview,
  then text, options or figure; the AI changes it) / save and close (it stays a draft) / discard (asks once
  more). **Publish**: yes, then the exam's link, import another paper or finish.
- **My exams** (`list_my_exams`): drafts, published or all, 20 a page, then one exam: open it on gamatrain,
  and for a draft preview, continue or discard it. `GET exams` lists a teacher's own exams of every status
  (it filters by status for gama-api's staff only, so the list is filtered here too); staff would get
  everyone's, so the caller's gama-api id (the JWT's `user_id`, `GetLegacyJwtUserIdAsync`) is always sent
  as `uid`, and a token without a readable `user_id` gets no list (`signInExpired`) rather than everyone's. A draft is status 6; published is 7 (published by its owner), 1 (confirmed) or 5 (a copy).
- **Sign out** (`sign_out`): see `docs/api/authentication.md`, "MCP connector (OAuth)": this connection's
  access token is refused from then on and its gama-api session ends; the draft stays.
Publishing, discarding and signing out ask once more (their tools ask when called without `confirmed`), and
the draft is only created from the details card's option.

Code: `Presentation/Mcp`, the MCP presentation layer (its own project next to the REST API, wired by
`AddGamaMcp`/`MapGamaMcp`): `ExamImportTools.cs` (the tools, thin; their public input schema is the ViewModels in
`Presentation/ViewModel/ExamImport`, mapped to the service's DTOs like a controller does, so a DTO change never
changes what the AI clients see), `ExamImportFlow.cs` (the steps and their wording), the widget, the OAuth
`McpController` and its pages. Then `IExamImportService`/
`ExamImportService` (the import), `ExamImportRules` (checks, and what a question is saved with),
`ExamImportText` (markup to HTML), the exam-builder methods of `ICoreProvider` (typed request DTOs in
`Core/Data/Dto/ExamImport`, each with the caller's own gama-api token as `SecretKey`). Only `CoreProvider`
knows gama-api's field names (`section`, `base`, `lesson`, `answer_full`, `tests[]`...).

**Nothing is stored here (since 2026-10-09).** The draft exam on gama-api (status 6) *is* the import:
its details, its question list and the questions themselves. There is no table, no stored gama-api token,
no background job and no cleanup job; every tool works with the caller's token from the MCP access token,
inside the call. (The first version kept the import in `ExamImports`/`ExamImportFigures` and uploaded it in
a Hangfire job paced to gama-api's rate limits; the review of the MCP PR replaced that, because a second
copy of the draft had to be kept in sync with gama-api and gama-api's limits are handled on its side.)

**The draft** (`set_exam_details`, `examId`). Every tool after it takes the draft's `examId` and first
reads the exam (`GET exams/{id}`): it must be the caller's (`owner`) and still a draft (status 6), so a
published exam, or someone else's, is never changed or deleted. The id is passed explicitly rather than
taken from `exams/current`, because gama-api's staff can have several drafts and `exams/current` returns
one of them. Home (`open_exams`) offers the caller's `exams/current` draft, to continue it; staff find their
other drafts in My exams.

**Exam details** (`set_exam_details`): all of them in every call (board, grade, course when the board has
courses, subject, paper and duration are required; component, session, year, title, level, negative
marking and past paper are optional), so a changed board, grade or course can never keep a subject from
before: every id is checked under its parent against gama-api's `types/list` (board = `section`, grade =
`base`, `course`, subject = `lesson`, paper = `exam_type`). Without `examId` it creates the draft
(`POST exams`); with it, it changes that draft (`PUT exams/{id}`), sending the optional fields left out as `0`,
because gama-api's edit keeps a field it isn't sent. Once the draft has questions, its board, grade, course and
subject can't change (`draftHasQuestions`): the questions were saved with them, and would be left mismatched. It returns the draft and the subject's
topics (`topic`); when there are any, every question needs one of them. The default title is subject +
component (or paper) + session. `find_past_papers` finds the matching past paper (`tests`) so the exam can
be linked to it (`paperID`, which gama-api applies when the draft is created), 15 a page (`page`), and since
2026-10-10 optionally of one paper type (`paperId`): a paper's classification is gama-api's `test_type` (a
board's list), a separate list from the exam's `exam_type` with the same titles (Paper 1..6), so the paper type
is turned into the board's classification with the same title, as `load_paper` does the other way, and sent as
`test_type`. For a teacher, gama-api's `GET tests` lists only the teacher's own papers. The guide has the AI
always look for the paper of a file the user hands over (the details card shows the link); it also
reads the duration from the cover or estimates it, without asking. gama-api allows a teacher
one unpublished draft: creating a second answers `existingDraft` with the current one, asked as continue it
(`open_review`) or discard it and create this one. Since 2026-10-10 `set_exam_details` first checks the details
without saving (`confirmed=false`, the details card) and creates the draft only from the card's option
(`confirmed=true`), for a file from the user and a paper from the directory alike. A required detail (board,
grade, course, subject, paper, duration) that is left out or not valid under its parent answers `pick{Detail}`
with gama-api's choices for it, which the connector asks the user to pick from.

**From a paper on gamatrain (staff, 2026-10-08).** An account whose gama-api JWT group is admin (1) or
sub-admin (7) is staff (`open_exams` says `staff`; read with `GetLegacyJwtGroupAsync`), and can skip
handing over files, from Home's "New exam from the Gamatrain directory":
- `search_papers`: a paper ID or words of its title (see "Guided prompts" above).
- `list_recent_papers`: the latest papers, newest first, 15 a page (`GET tests`, `sortby=subdatedesc`;
  gama-api lists every paper to its staff, only their own to a teacher), to pick one.
- `load_paper` (read-only): one paper (`GET tests/{id}`) with its exam details for `set_exam_details`:
  board, grade, course, subject, year, session, the paper's id (`pastPaperId`) and the paper type, matched
  by title from the paper's classification (`test_type`, e.g. Paper 2) to an `exam_type`. The AI adds the
  duration and the component code from the cover, and the details card shows them already filled in. A paper
  that already has an online exam is asked about first.
- Each file (question paper PDF and Word, mark scheme, extra files such as inserts) gets gama-api's own
  temporary download link (`GET tests/download/{id}/{type}[/{extraId}]`, about an hour; one call a
  second per address, so 1.2 s apart, and `gone` is retried). The AI reads every file: with a shell it
  downloads them, otherwise it opens the links or asks the user to attach them. Nothing is downloaded
  or read here.
- A file gets a link only when gama-api gives it to the caller for free: they own the paper or manage
  it (the detail's `owner`/`admin`), or it has no price or is already paid. For anyone else the download
  call is a purchase (see `content-delivery.md`), and gama-api lets it through from this API's address as
  one this API already charged for. Known gap: a sub-admin with gama-api's `tests_detail` permission but
  not `tests_download_*` counts as managing the paper.

**Figures** (`add_figure`): PNG or JPEG only (gama-api's question images), at most 5 MB. Each image is
uploaded to gama-api at once (`POST upload`) and the AI gets gama-api's file key for a question's `figure`,
`optionFigures` (A–D) or `answerFigure`. gama-api moves an upload into the question it is saved with, so
a key works for one question: an image two questions use is uploaded twice. A figure left out when a
question is changed keeps its image (gama-api's edit). The image comes as a file attached in ChatGPT, a
public link or base64, or through a signed upload link (`get_figure_upload_link`, 2 hours, multipart
`file`, e.g. `curl` from Claude Code); the link carries the caller's gama-api token, protected with Data
Protection, so it can only upload images for them, and stops working when that connection signs out (`sign_out`,
the same deny-list as the access token). A link is downloaded by the service through `IWebDownloadProvider`, only
from a public address, checked on the address actually connected to, so it can't reach this server's network (see
`docs/architecture/cross-cutting-concerns.md`, "Outbound HTTP to user-given links"). The AI prepares each
image itself by the guide's figure rules (2026-10-10): the wording around a drawing goes into the question
text instead of the image, watermarks are removed, and every figure of a paper has one scale (5 px per mm of
paper, at most 900 × 1200 px, labels at least 12 px tall); this backend doesn't resize or check them.

**Questions** (`save_questions(examId, questions)`, at most 40 a call, in order). The checks mirror
gama-api's `Examtest_lib::checkRequiredFields`: `fourchoice` needs 4 options (or 4 option images),
`twochoice`/`tf` 2 (`tf` defaults to True/False), and a correct letter; `descriptive`, `shortanswer` and
`blank` need an answer (text or image). Since 2026-10-10 the guide also has the AI write a worked solution
(`answer`, gama-api's `answer_full`) for every question, multiple choice included, and solve the question
itself where the mark scheme has none or there is no mark scheme (`answerSource: ai`, flagged for review),
instead of asking the user first. Each question gets a status:
- **blocked**: can't be saved as it is (no answer, no correct letter, a missing topic...): not sent;
- **review**: saved, but a human should look (an AI-written answer, a review note, a figure the text
  mentions but isn't attached, duplicate options, very long text);
- **saved**; **skipped** (`skip: true`, not sent); **removed** (`skip: true` with an id: taken off the
  draft); **failed** (gama-api refused it; its message is in `error`).
A question without `id` is created (`POST examTests`) and added to the draft; with an `id` it is changed
(`PUT examTests/{id}`). Only a question on the draft can be changed or removed, so an id can't reach
someone else's question. The new questions are added to the draft (`PUT exams/tests/{id}` with `tests[]`,
which replaces the whole list) by reading the list and writing it back under a lock per draft
(`ICacheProvider.LockAsync`, Redis, so across instances too): without it two saves at once each wrote the list
they had read and one batch was lost. If adding them fails (or the draft stays busy for 20 s, `draftBusy`), the
questions just created are deleted and the batch can be saved again. Taking a question off the draft uses the
same lock. (An edit in gamatrain's own exam builder at the same moment is not covered: it replaces the list too.) The text markup (paragraphs, `**bold**`, `__underline__`) becomes the HTML subset gama-api
keeps (`p b u br`); TeX stays as it is for MathJax. A power written with a bare caret outside TeX (`m s^-1`,
`10^12`, `x^(2)`, `s^{-1}`) becomes inline TeX (`m s\(^{-1}\)`), since gama-api strips `sup` and the caret was
otherwise shown and exported as typed (seen in exam 2293's options, 2026-10-09; `ExamImportTextTests`); the guide also
asks for TeX in options and answers. Each question's `resource` is the board, the exam
title and its number (e.g. `Cambridge Mathematics 9709/12 — May/June 2024, Q3(b)`).

`remove_question` (and `skip` with an id) takes a question off the draft, and deletes it only when the
caller made it and it isn't reviewed into the question bank yet (`owner`, status 0): a question taken from
the bank stays there.

**Preview** (`show_preview(examId)`): the draft's questions as gama-api stores them (`examTests?exam_id=`,
in exam order), in ChatGPT as a card in the chat (an MCP Apps widget, `Mcp/ExamImportWidget.html`; its
images load from gama-api's origin), and everywhere as the draft on gamatrain's exam builder
(`Mcp:ExamDraftUrl`, `test-maker/edit/{id}`, which needs the teacher signed in to gamatrain.com).

**Publish** (`publish_exam(examId, confirmed)`): `PUT exams/publish/{id}`; gamatrain needs at least 1
question. **Discard** (`discard_draft(examId, confirmed)`): deletes the draft (`DELETE exams/{id}`) and, by
default, the questions on it the caller made that aren't in the question bank yet. A published exam is
not a draft, so it is never deleted here.

**gama-api's rate limits** (one new question per user every 20 s, one new exam every 60 s) are handled on
gama-api's side for this connector: nothing here waits for them. Until gama-api lets these calls through,
`save_questions` reports the questions after the first of a batch as `failed` (`rateLimit-addnew`).

Limits to know: the connector has been run end to end only against a local stand-in for gama-api that
follows its envelope; a real create/publish on core.gamatrain.com still needs a teacher account and a
sub-admin account to test.

