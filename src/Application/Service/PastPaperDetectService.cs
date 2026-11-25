namespace GamaEdtech.Application.Service
{
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Text.RegularExpressions;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.PastPaper;
    using GamaEdtech.Data.Dto.Taxonomy;
    using GamaEdtech.Domain.Entity;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;

    using UglyToad.PdfPig;

    using static GamaEdtech.Common.Core.Constants;

    /// <inheritdoc cref="IPastPaperDetectService"/>
    public sealed partial class PastPaperDetectService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor,
        Lazy<ILogger<PastPaperDetectService>> logger, Lazy<IAiService> aiService)
        : ServiceBase<PastPaperDetectService>(unitOfWorkProvider, httpContextAccessor, logger), IPastPaperDetectService
    {
        /// <summary>The cover page (and the next, where a code sometimes sits) is all that is read.</summary>
        private const int PagesRead = 2;

        /// <summary>What the AI model gets of the page: enough for a cover, little for the cost.</summary>
        private const int AiTextLimit = 3000;

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private const string AiInstructions = """
            You read the first page of an exam paper's PDF (its text, possibly out of order) and answer only with JSON:
            {"board": string|null, "grade": string|null, "subject": string|null, "code": string|null, "paper": number|null,
             "variant": string|null, "year": number|null, "month": number|null, "isMarkScheme": boolean}
            board: the exam board (e.g. Cambridge, Edexcel, AQA, OCR). grade: the qualification level as printed (e.g.
            "AS & A Level", "IGCSE", "O Level", "GCSE"). subject: the subject name. code: the syllabus/specification code
            (e.g. 9700, 8300, 9MA0). paper: the paper or component number. variant: the variant if printed, else null.
            year and month: the exam session (month 1-12). isMarkScheme: true when this is a mark scheme. Use null for
            anything the page doesn't say; never guess.
            """;

        // Cambridge's session words, and the month this site files each session under.
        private static readonly (string Pattern, int Month, string Words)[] Sessions =
        [
            ("February\\s*/\\s*March", 3, "February March"),
            ("May\\s*/\\s*June", 6, "May June"),
            ("October\\s*/\\s*November", 11, "October November"),
        ];

        public async Task<ResultData<PastPaperDetectionDto>> DetectAsync(byte[] pdf, bool askAi = true, CancellationToken cancellationToken = default)
        {
            try
            {
                var text = CoverText(pdf);
                if (string.IsNullOrWhiteSpace(text))
                {
                    return NotFound();
                }

                var kind = ReadFileKind(text);
                var facts = ReadPrintedCodes(text);
                var source = "pattern";
                if (facts is null && askAi)
                {
                    facts = await AskAiAsync(text, cancellationToken);
                    source = "ai";
                }

                facts ??= new(null, null, null, null, null, null, null, null, null, false);
                if (kind is not null)
                {
                    facts = facts with { IsMarkScheme = facts.IsMarkScheme || kind == FileKind.MarkScheme };
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var detection = await MapAsync(uow, facts, source);
                await MapFileKindAsync(uow, detection, kind ?? (facts.IsMarkScheme ? FileKind.MarkScheme : null));
                // "question" is only the default; a page with nothing recognised says nothing.
                return detection.Board is null && detection.Subject is null && detection.Year is null && detection.FileRole == "question"
                    ? NotFound()
                    : new(OperationResult.Succeeded) { Data = detection };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<JsonNode>> WithDetectionAsync([NotNull] ResultData<JsonNode> upload, byte[] pdf, bool question, CancellationToken cancellationToken = default)
        {
            if (upload.OperationResult is not OperationResult.Succeeded || upload.Data is not JsonArray { Count: > 0 } items || items[0] is not JsonObject item)
            {
                return upload;
            }

            ArgumentNullException.ThrowIfNull(pdf);

            // Not a PDF: nothing to read (a Word file's text isn't read yet).
            if (pdf.Length < 5 || !pdf.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
            {
                return upload;
            }

            var detection = await DetectAsync(pdf, askAi: question, cancellationToken);
            if (detection.Data is not null)
            {
                item["detected"] = JsonSerializer.SerializeToNode(detection.Data, JsonOptions);
            }

            return upload;
        }

        /// <summary>What a file says it is on its first page (a question paper says none of these).</summary>
        internal enum FileKind
        {
            MarkScheme,
            Insert,
            ConfidentialInstructions,
            Transcript,
            PreReleaseMaterial,
            SolvedPaper,
        }

        /// <summary>The kind a file's first page prints, most specific first; none for a question paper. Cambridge prints these
        /// headings in capitals, so "INSERT" and "TRANSCRIPT" only count in capitals (a question can say "insert").</summary>
        internal static FileKind? ReadFileKind(string text) =>
            KindPatterns.Where(t => t.Pattern.IsMatch(text)).Select(t => (FileKind?)t.Kind).FirstOrDefault();

        private static readonly (Regex Pattern, FileKind Kind)[] KindPatterns =
        [
            (ConfidentialRegex(), FileKind.ConfidentialInstructions),
            (PreReleaseRegex(), FileKind.PreReleaseMaterial),
            (TranscriptRegex(), FileKind.Transcript),
            (MarkSchemeRegex(), FileKind.MarkScheme),
            (InsertRegex(), FileKind.Insert),
            (SolvedRegex(), FileKind.SolvedPaper),
        ];

        // The extra file type each kind is filed under, by a word of its title (titles vary: "Confidental Instructions").
        private static readonly (FileKind Kind, string TitleWord)[] ExtraTypeWords =
        [
            (FileKind.Insert, "INSERT"),
            (FileKind.ConfidentialInstructions, "CONFIDEN"),
            (FileKind.Transcript, "TRANSCRIPT"),
            (FileKind.PreReleaseMaterial, "PRERELEASE"),
            (FileKind.SolvedPaper, "SOLVED"),
        ];

        /// <summary>Sets what the file is: a mark scheme is the answer, the other kinds an extra file of their type, and a page
        /// with none of them the question paper.</summary>
        private static async Task MapFileKindAsync(IUnitOfWork uow, PastPaperDetectionDto detection, FileKind? kind)
        {
            switch (kind)
            {
                case null:
                    detection.FileRole = "question";
                    return;
                case FileKind.MarkScheme:
                    detection.FileRole = "answer";
                    return;
            }

            detection.FileRole = "extra";
            var word = ExtraTypeWords.First(t => t.Kind == kind).TitleWord;
            var types = await uow.GetRepository<LegacyTaxonomyItem, int>().GetManyQueryable(t => t.Kind == LegacyTaxonomyKinds.ExtraFileType && !t.IsDeleted)
                .OrderBy(t => t.ListOrder).Select(t => new { t.Id, t.Title }).ToListAsync();
            var type = types.FirstOrDefault(t => t.Title is not null && Squash(t.Title).Contains(word, StringComparison.Ordinal));
            if (type is not null)
            {
                detection.FileType = Item(type.Id, type.Title);
            }
        }

        /// <summary>What a paper's cover says, before it is matched to the lists.</summary>
        internal sealed record PaperFacts(string? Board, string? Grade, string? Subject, string? Code, string? Component, int? Paper, string? Variant,
            int? Year, int? Month, bool IsMarkScheme, bool Predicted = false, IReadOnlyList<string>? Codes = null);

        /// <summary>The first pages' words, in reading order, one space apart. Empty for a PDF that can't be read or has no
        /// text (a scan).</summary>
        internal static string CoverText(byte[] pdf)
        {
            try
            {
                using var document = PdfDocument.Open(pdf);
                return string.Join('\n', document.GetPages().Take(PagesRead).Select(page => string.Join(' ', page.GetWords().Select(word => word.Text))));
            }
            catch (Exception exc) when (exc is not OutOfMemoryException)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// A Cambridge cover: the component code (<c>9700/11</c>), the session (<c>May/June 2025</c>) and the level. A
        /// component <c>XY</c> is paper X, variant Y; <c>0Y</c> or a single digit (our predicted papers: <c>0610/5</c>) is that
        /// paper with no variant. A Cambridge cover without such a code but with "Paper N" (<c>Physics Paper 1 ... 0625</c>)
        /// gives the paper and its 4-digit numbers as possible codes, which only count when one is a subject's code. "Predicted
        /// Paper" marks the site's predicted papers. None when the page has neither: the AI reads it then.
        /// </summary>
        internal static PaperFacts? ReadPrintedCodes(string text)
        {
            var cambridge = text.Contains("Cambridge", StringComparison.OrdinalIgnoreCase);
            var code = CodeRegex().Match(text);
            int? paper;
            string? variant;
            string? component = null;
            string? syllabus = null;
            List<string>? candidates = null;
            if (code.Success)
            {
                component = code.Groups[2].Value;
                syllabus = code.Groups[1].Value;
                (paper, variant) = component.Length == 1 || component[0] == '0'
                    ? (component[^1] - '0', "nv")
                    : (component[0] - '0', component[1].ToString());
            }
            else
            {
                var paperNumber = PaperNumberRegex().Match(text);
                if (!cambridge || !paperNumber.Success)
                {
                    return null;
                }

                paper = paperNumber.Groups[1].Value[0] - '0';
                variant = null;
                candidates = [.. FourDigitsRegex().Matches(text).Select(t => t.Value)
                    .Where(t => int.Parse(t, CultureInfo.InvariantCulture) is < 1990 or > 2100).Distinct()];
                if (candidates.Count == 0)
                {
                    return null;
                }
            }

            int? year = null;
            int? month = null;
            foreach (var (pattern, sessionMonth, _) in Sessions)
            {
                var session = Regex.Match(text, $"{pattern}\\s+(\\d{{4}})", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
                if (session.Success)
                {
                    (year, month) = (int.Parse(session.Groups[1].Value, CultureInfo.InvariantCulture), sessionMonth);
                    break;
                }
            }

            string? grade = null;
            if (AsALevelRegex().IsMatch(text))
            {
                grade = "AS & A Level";
            }
            else if (IgcseRegex().IsMatch(text))
            {
                grade = text.Contains("(9–1)", StringComparison.Ordinal) || text.Contains("(9-1)", StringComparison.Ordinal) ? "9–1 IGCSE" : "IGCSE";
            }
            else if (OLevelRegex().IsMatch(text))
            {
                grade = "O-Level";
            }

            return new(
                cambridge ? "Cambridge" : null,
                grade,
                null,
                syllabus,
                component,
                paper,
                variant,
                year,
                month,
                MarkSchemeRegex().IsMatch(text),
                PredictedRegex().IsMatch(text),
                candidates);
        }

        /// <summary>The page's text read by the configured AI model, for a cover without a code the patterns know. None when
        /// the model isn't configured or answers nothing usable.</summary>
        private async Task<PaperFacts?> AskAiAsync(string text, CancellationToken cancellationToken)
        {
            var answer = await aiService.Value.CompleteAsync(new()
            {
                SystemPrompt = AiInstructions,
                UserPrompt = text.Length > AiTextLimit ? text[..AiTextLimit] : text,
                MaxTokens = 300,
                Temperature = 0,
            });
            cancellationToken.ThrowIfCancellationRequested();
            return answer.OperationResult is OperationResult.Succeeded ? ParseAi(answer.Data?.Content) : null;
        }

        /// <summary>The model's JSON (also inside a code fence), or none.</summary>
        internal static PaperFacts? ParseAi(string? content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            var start = content.IndexOf('{', StringComparison.Ordinal);
            var end = content.LastIndexOf('}');
            if (start < 0 || end <= start)
            {
                return null;
            }

            try
            {
                using var json = JsonDocument.Parse(content[start..(end + 1)]);
                var root = json.RootElement;
                string? Text(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } s ? s.Trim() : null;
                int? Number(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n) ? n : null;
                var month = Number("month");
                return new(Text("board"), Text("grade"), Text("subject"), Text("code"), null, Number("paper"), Text("variant"), Number("year"),
                    month is >= 1 and <= 12 ? month : null,
                    root.TryGetProperty("isMarkScheme", out var markScheme) && markScheme.ValueKind == JsonValueKind.True);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>The facts as the add form's lookup items: the board by name, the subject by its code (or name) under the
        /// board's grades (which also gives the grade), the paper type "Paper N" this board's papers use most, the variant.</summary>
        private static async Task<PastPaperDetectionDto> MapAsync(IUnitOfWork uow, PaperFacts facts, string source)
        {
            var items = uow.GetRepository<LegacyTaxonomyItem, int>();
            PastPaperDetectionDto detection = new()
            {
                Source = source,
                IsMarkScheme = facts.IsMarkScheme,
                Year = facts.Year is >= 1990 and <= 2100 ? facts.Year : null,
                Month = facts.Month,
                Code = facts.Component is null ? facts.Code : $"{facts.Code}/{facts.Component}",
            };

            var boards = await items.GetManyQueryable(t => t.Kind == LegacyTaxonomyKinds.Board && !t.IsDeleted).Select(t => new { t.Id, t.Title }).ToListAsync();
            var board = facts.Board is null ? null : boards.FirstOrDefault(t => Same(t.Title, facts.Board));
            if (board is null)
            {
                return detection;
            }

            detection.Board = Item(board.Id, board.Title);
            var boardKey = Invariant(board.Id);
            var grades = await items.GetManyQueryable(t => t.Kind == LegacyTaxonomyKinds.Grade && !t.IsDeleted && t.Parents!.Any(p => p.ParentKey == boardKey))
                .Select(t => new { t.Id, t.Title }).ToListAsync();
            var grade = facts.Grade is null ? null : grades.FirstOrDefault(t => Same(t.Title, facts.Grade));

            // The subject, by its code in its title ("Biology (9700)"), else by its name; its parent key is "course,grade".
            var gradeKeys = (grade is null ? grades : [grade]).Select(t => $"0,{Invariant(t.Id)}").ToList();
            var subjects = await items.GetManyQueryable(t => t.Kind == LegacyTaxonomyKinds.Subject && !t.IsDeleted && t.Parents!.Any(p => gradeKeys.Contains(p.ParentKey!)))
                .Select(t => new SubjectRow(t.Id, t.Title, t.Parents!.Select(p => p.ParentKey!).ToList())).ToListAsync();
            // A code printed without its component is only a guess among the page's numbers: the one that is a subject's code.
            var subject = (facts.Code is { } code ? BySubjectCode(subjects, code) : null)
                ?? (facts.Codes ?? []).Select(t => BySubjectCode(subjects, t)).FirstOrDefault(t => t is not null)
                ?? (facts.Subject is { } name ? subjects.FirstOrDefault(t => Same(SubjectName(t.Name), name)) : null);
            if (subject is not null && detection.Code is null && CodeSuffixRegex().Match(subject.Name ?? string.Empty) is { Success: true } suffix)
            {
                detection.Code = suffix.Value.Trim().Trim('(', ')');
            }
            if (subject is not null)
            {
                detection.Subject = Item(subject.Id, subject.Name);
                grade ??= grades.FirstOrDefault(t => subject.ParentKeys.Contains($"0,{Invariant(t.Id)}"));
            }

            if (grade is not null)
            {
                detection.Grade = Item(grade.Id, grade.Title);
            }

            if (facts.Predicted)
            {
                // The site's predicted papers are filed under the board's "Predicted Papers" type, not "Paper N".
                var predicted = await items.GetManyQueryable(t => t.Kind == LegacyTaxonomyKinds.Classification && !t.IsDeleted && t.Parents!.Any(p => p.ParentKey == boardKey))
                    .Select(t => new { t.Id, t.Title }).ToListAsync();
                if (predicted.FirstOrDefault(t => t.Title is not null && Squash(t.Title).StartsWith("PREDICTED", StringComparison.Ordinal)) is { } type)
                {
                    detection.Classification = Item(type.Id, type.Title);
                }
            }
            else if (facts.Paper is int paper)
            {
                var papers = await items.GetManyQueryable(t => t.Kind == LegacyTaxonomyKinds.Classification && !t.IsDeleted && t.IsPaper == true
                        && t.Parents!.Any(p => p.ParentKey == boardKey))
                    .Select(t => new { t.Id, t.Title, t.ListOrder }).ToListAsync();
                var named = papers.Where(t => Same(t.Title, $"Paper {Invariant(paper)}")).ToList();
                if (named.Count > 0)
                {
                    // A board has several "Paper 1" items; the one its papers use most is the one the site files under.
                    var ids = named.Select(t => (int?)t.Id).ToList();
                    var used = await uow.GetRepository<PastPaper>().GetManyQueryable(t => t.CoreBoardId == board.Id && ids.Contains(t.CorePaperTypeId))
                        .GroupBy(t => t.CorePaperTypeId).Select(t => new { Id = t.Key, Count = t.Count() }).ToListAsync();
                    var chosen = named.OrderByDescending(t => used.FirstOrDefault(u => u.Id == t.Id)?.Count ?? 0).ThenBy(t => t.ListOrder).First();
                    detection.Classification = Item(chosen.Id, chosen.Title);
                }
            }

            if (facts.Variant is { } variantTitle)
            {
                var variant = await items.GetManyQueryable(t => t.Kind == LegacyTaxonomyKinds.Variant && !t.IsDeleted && t.Title == variantTitle)
                    .Select(t => new { t.Id, t.Title }).FirstOrDefaultAsync();
                if (variant is not null)
                {
                    detection.Variant = Item(variant.Id, variant.Title);
                }
            }

            detection.Title = Title(detection, facts);
            return detection;
        }

        private sealed record SubjectRow(int Id, string? Name, List<string> ParentKeys);

        private static SubjectRow? BySubjectCode(List<SubjectRow> subjects, string code) =>
            subjects.FirstOrDefault(t => (t.Name ?? string.Empty).TrimEnd().EndsWith($"({code})", StringComparison.OrdinalIgnoreCase));

        /// <summary>The site's usual title: <c>Biology 9700/11 May June 2025</c>; none without a subject.</summary>
        internal static string? Title(PastPaperDetectionDto detection, PaperFacts facts)
        {
            var subject = SubjectName(detection.Subject?.Title) ?? facts.Subject;
            if (subject is null)
            {
                return null;
            }

            var session = Sessions.FirstOrDefault(t => t.Month == detection.Month).Words
                ?? (detection.Month is int month ? CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month) : null);
            // A predicted paper reads as the site names them: "Predicted Paper Biology 0610 Paper 5".
            var year = detection.Year is int value ? Invariant(value) : null;
            var paper = facts.Paper is int number ? $"Paper {Invariant(number)}" : null;
            string?[] parts = facts.Predicted
                ? ["Predicted Paper", subject, detection.Code?.Split('/')[0], paper, session, year]
                : [subject, detection.Code ?? detection.Classification?.Title, session, year];
            return string.Join(' ', parts.Where(t => !string.IsNullOrWhiteSpace(t)));
        }

        /// <summary>A subject's name without its code: "Biology (9700)" is "Biology".</summary>
        private static string? SubjectName(string? title) => title is null ? null : CodeSuffixRegex().Replace(title, string.Empty).Trim();

        /// <summary>Two titles mean the same: letters and digits only, any case ("O-Level" is "O Level").</summary>
        private static bool Same(string? a, string? b) => a is not null && b is not null && Squash(a) == Squash(b);

        private static string Squash(string text) => new([.. text.Normalize().ToUpperInvariant().Where(char.IsLetterOrDigit)]);

        private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static PastPaperDetectionDto.DetectedItemDto Item(int id, string? title) => new() { Id = id, Title = title?.Trim() ?? string.Empty };

        private static ResultData<PastPaperDetectionDto> NotFound() =>
            new(OperationResult.NotFound) { Errors = [new() { Message = "Nothing on the paper was recognised." }] };

        [GeneratedRegex(@"\b(\d{4})/(\d{1,2})\b")]
        private static partial Regex CodeRegex();

        [GeneratedRegex(@"\bAS\s*(&|and)\s*A\s*Level\b", RegexOptions.IgnoreCase)]
        private static partial Regex AsALevelRegex();

        [GeneratedRegex(@"\bIGCSE", RegexOptions.IgnoreCase)]
        private static partial Regex IgcseRegex();

        [GeneratedRegex(@"\bO\s*-?\s*Level\b", RegexOptions.IgnoreCase)]
        private static partial Regex OLevelRegex();

        [GeneratedRegex(@"\bSolved\s+(Past\s+)?Papers?\b|\bWorked\s+solutions?\b|\bFully\s+solved\b", RegexOptions.IgnoreCase)]
        private static partial Regex SolvedRegex();

        [GeneratedRegex(@"\bPaper\s+(\d)\b", RegexOptions.IgnoreCase)]
        private static partial Regex PaperNumberRegex();

        [GeneratedRegex(@"(?<![\d/])\d{4}(?![\d/])")]
        private static partial Regex FourDigitsRegex();

        [GeneratedRegex(@"\bPredicted\s+Paper\b", RegexOptions.IgnoreCase)]
        private static partial Regex PredictedRegex();

        [GeneratedRegex(@"\bMARK\s+SCHEME\b", RegexOptions.IgnoreCase)]
        private static partial Regex MarkSchemeRegex();

        [GeneratedRegex(@"\bCONFIDENTIAL\s+INSTRUCTIONS\b", RegexOptions.IgnoreCase)]
        private static partial Regex ConfidentialRegex();

        [GeneratedRegex(@"\bPRE-?\s*RELEASED?\s+MATERIAL\b", RegexOptions.IgnoreCase)]
        private static partial Regex PreReleaseRegex();

        [GeneratedRegex(@"\bTRANSCRIPT\b")]
        private static partial Regex TranscriptRegex();

        [GeneratedRegex(@"\bINSERT\b")]
        private static partial Regex InsertRegex();

        [GeneratedRegex(@"\s*\(\w+\)\s*$")]
        private static partial Regex CodeSuffixRegex();
    }
}
