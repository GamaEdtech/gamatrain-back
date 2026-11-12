namespace GamaEdtech.Application.Service
{
    using System.Globalization;
    using System.Text.RegularExpressions;

    using GamaEdtech.Data.Dto.ExamImport;

    using static GamaEdtech.Data.Dto.ExamImport.ExamImportReportDto;

    /// <summary>
    /// What an imported question must meet before it goes to gama-api (the same required fields as gama-api's
    /// <c>Examtest_lib::checkRequiredFields</c>, plus what a human should look at), and what a question is saved with on
    /// a draft exam (in gamatrain's terms; <c>CoreProvider</c> turns it into gama-api's fields). Pure functions;
    /// <see cref="ExamImportService"/> calls.
    /// </summary>
    internal static partial class ExamImportRules
    {
        /// <summary>gama-api's <c>staticValues('examMinTestsNum')</c> on gamatrain (LANG=en).</summary>
        public const int MinQuestionsToPublish = 1;

        private const string Letters = "ABCD";
        private const string ErrorSeverity = "error";
        private const string ReviewSeverity = "review";
        private const int MaxTextLength = 5000;

        /// <summary>A choice question whose options are only the letters, with its choices in the question's figure.</summary>
        public const string LettersOnlyOptionsCode = "lettersOnlyOptions";

        // gama-api keeps a question's image when it is saved again without one, so a question saved with the choices in
        // its figure can't be turned into option images in place: it has to be removed and saved again as a new one.
        private const string LettersOnlyOptionsMessage = "The options are only the letters and the choices are in the question's figure. If the choices are separate "
            + "drawings, graphs or photos that can each be cut out (a key or legend they share goes under each one), they must be option "
            + "figures: take this question off with remove_question and save it again without its id, with optionFigures (A-D, no letters "
            + "in the images) and a figure only for the stem's own diagram (Gamatrain can't take an image off a saved question). If they are "
            + "parts of one drawing marked A-D, it is right as it is.";

        private static readonly Dictionary<string, int> ChoiceTypes = new(StringComparer.Ordinal) { ["fourchoice"] = 4, ["twochoice"] = 2, ["tf"] = 2 };
        private static readonly HashSet<string> OpenTypes = new(StringComparer.Ordinal) { "descriptive", "shortanswer", "blank" };
        private static readonly HashSet<string> AnswerSources = new(StringComparer.Ordinal) { "markScheme", "paper", "ai", "user" };
        private static readonly Dictionary<string, string> CorrectAliases = new(StringComparer.Ordinal)
        {
            ["TRUE"] = "A",
            ["T"] = "A",
            ["FALSE"] = "B",
            ["F"] = "B",
            ["1"] = "A",
            ["2"] = "B",
            ["3"] = "C",
            ["4"] = "D",
        };

        public static IReadOnlyDictionary<string, string> TypeLabels { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["fourchoice"] = "Multiple choice (4)",
            ["twochoice"] = "Multiple choice (2)",
            ["tf"] = "True / False",
            ["descriptive"] = "Written answer",
            ["shortanswer"] = "Short answer",
            ["blank"] = "Fill in the blank",
        };

        /// <summary>The number of options a choice type has; 0 for the open types.</summary>
        public static int OptionCount(string type) => ChoiceTypes.GetValueOrDefault(type);

        public static string Letter(int index) => Letters[index].ToString();

        /// <summary>The problems that make a question impossible to store at all.</summary>
        public static IEnumerable<string> Validate(ExamImportQuestionDto question)
        {
            var label = string.IsNullOrWhiteSpace(question.Number) ? "A question" : $"Question {question.Number.Trim()}";
            if (string.IsNullOrWhiteSpace(question.Number))
            {
                yield return "A question has no number.";
            }
            else if (question.Number.Trim().Length > 50)
            {
                yield return $"{label}: the number is longer than 50 characters.";
            }

            if (!ChoiceTypes.ContainsKey(question.Type?.Trim() ?? string.Empty) && !OpenTypes.Contains(question.Type?.Trim() ?? string.Empty))
            {
                yield return $"{label}: type must be fourchoice, twochoice, tf, descriptive, shortanswer or blank.";
            }

            if (!string.IsNullOrWhiteSpace(question.AnswerSource) && !AnswerSources.Contains(question.AnswerSource.Trim()))
            {
                yield return $"{label}: answerSource must be markScheme, paper, ai or user.";
            }

            if (question.Marks is < 0 or > 100)
            {
                yield return $"{label}: marks must be 0-100.";
            }

            if (question.Level is < 1 or > 3)
            {
                yield return $"{label}: level must be 1 (easy), 2 (medium) or 3 (hard).";
            }
        }

        /// <summary>Trims the text, normalizes the correct letter (True/T/1 → A...) and drops empty ids and notes.</summary>
        public static ExamImportQuestionDto Normalize(ExamImportQuestionDto question) => new()
        {
            Id = question.Id > 0 ? question.Id : null,
            Number = question.Number.Trim(),
            Type = question.Type.Trim(),
            Text = Clean(question.Text),
            Options = question.Options?.Select(t => t?.Trim() ?? string.Empty).ToList(),
            Correct = NormalizeCorrect(question.Correct),
            Answer = Clean(question.Answer),
            AnswerSource = Clean(question.AnswerSource),
            Marks = question.Marks,
            TopicId = question.TopicId > 0 ? question.TopicId : null,
            Level = question.Level,
            NeedsFigure = question.NeedsFigure,
            Figure = Clean(question.Figure),
            OptionFigures = NonEmpty(question.OptionFigures),
            AnswerFigure = Clean(question.AnswerFigure),
            ReviewNotes = NonEmpty(question.ReviewNotes),
            Skip = question.Skip,
        };

        /// <summary>Blocking errors (cannot be saved as is) and review flags (a human should look). <paramref name="topics"/>
        /// are the exam subject's topics.</summary>
        public static IReadOnlyList<IssueDto> Check(ExamImportQuestionDto question, IReadOnlyCollection<ExamImportOptionDto> topics)
        {
            List<IssueDto> issues = [];
            void Error(string code, string message) => issues.Add(new() { Severity = ErrorSeverity, Code = code, Message = message });
            void Review(string code, string message) => issues.Add(new() { Severity = ReviewSeverity, Code = code, Message = message });

            var hasFigure = question.Figure is not null;
            if (question.Text is null && !hasFigure)
            {
                Error("missingText", "The question has no text and no figure.");
            }
            else if (question.Text is null)
            {
                Review("figureOnly", "No question text; \"Refer to the figure.\" will be used.");
            }

            var options = question.Options ?? [];
            var optionFigures = question.OptionFigures ?? [];
            if (ChoiceTypes.TryGetValue(question.Type, out var need))
            {
                if (optionFigures.Count > 0)
                {
                    if (optionFigures.Count != need)
                    {
                        Error("optionFigures", $"{question.Type} needs {need} option figures, got {optionFigures.Count}.");
                    }
                }
                else if (question.Type == "tf" && options.Count == 0)
                {
                    // Defaults to True/False.
                }
                else if (options.Count != need)
                {
                    Error("optionCount", $"{question.Type} needs {need} options, got {options.Count}.");
                }
                else if (options.Any(string.IsNullOrEmpty))
                {
                    Error("emptyOption", "One or more options are empty.");
                }
                else if (options.Distinct(StringComparer.OrdinalIgnoreCase).Count() != options.Count)
                {
                    Review("duplicateOptions", "Two options have the same text.");
                }
                else if (hasFigure && LettersOnly(options))
                {
                    Review(LettersOnlyOptionsCode, LettersOnlyOptionsMessage);
                }

                var validLetters = Letters[..need];
                if (question.Correct is null)
                {
                    Error(IssueDto.MissingCorrectCode, "No correct answer (no mark scheme entry). Add it or skip the question.");
                }
                else if (question.Correct.Length != 1 || !validLetters.Contains(question.Correct, StringComparison.Ordinal))
                {
                    Error("badCorrect", $"The correct answer must be one of {string.Join(", ", validLetters.ToCharArray())}.");
                }
            }
            else
            {
                if (options.Count > 0 || optionFigures.Count > 0)
                {
                    Review("unusedOptions", $"{question.Type} questions have no options; they will be ignored.");
                }

                if (question.Answer is null && question.AnswerFigure is null)
                {
                    Error(IssueDto.MissingAnswerCode, $"{question.Type} questions need a model answer (mark scheme). Add it or skip the question.");
                }
            }

            if (question.AnswerSource == "ai")
            {
                Review(IssueDto.AiAnswerCode, "The answer was written by the AI, not taken from the mark scheme.");
            }

            if (topics.Count > 0 && question.TopicId is null)
            {
                Error("missingTopic", "This subject has topics; pick one for the question.");
            }
            else if (question.TopicId is not null && topics.Count > 0 && !topics.Any(t => t.Id == question.TopicId))
            {
                Error("badTopic", $"Topic {question.TopicId} is not a topic of the chosen subject.");
            }

            if (!hasFigure && question.NeedsFigure)
            {
                Review("missingFigure", "The question needs a figure but none is attached yet.");
            }
            else if (!hasFigure && question.Text is not null && AssetHintRegex().IsMatch(question.Text))
            {
                Review("missingFigure", "The text mentions a diagram, table or graph but no figure is attached.");
            }

            if (ExamImportText.PlainLength(ExamImportText.ToHtml(question.Text)) > MaxTextLength)
            {
                Review("longText", "Very long question text; check it was split correctly.");
            }

            foreach (var note in question.ReviewNotes ?? [])
            {
                Review("aiNote", note);
            }

            return issues;
        }

        /// <summary>The question's status: skipped, blocked (it has an error), review (it has flags) or saved.</summary>
        public static string StatusOf(ExamImportQuestionDto question, IReadOnlyList<IssueDto> issues) => question switch
        {
            { Skip: true } => SkippedStatus,
            _ when issues.Any(t => t.Severity == ErrorSeverity) => BlockedStatus,
            _ when issues.Count > 0 => ReviewStatus,
            _ => SavedStatus,
        };

        public static string? SessionLabel(ExamImportDraftDto details) => SessionLabel(details.SessionMonth, details.Year);

        public static string? SessionLabel(int? month, int? year)
        {
            var when = string.Join(' ', new[] { ExamImportDraftDto.SessionMonths.GetValueOrDefault(month ?? 0), year?.ToString(CultureInfo.InvariantCulture) }.Where(t => !string.IsNullOrEmpty(t)));
            return when.Length == 0 ? null : when;
        }

        /// <summary>The session's months in short and the year for the default title, e.g. "Oct Nov 2025".</summary>
        private static string? ShortSession(ExamImportDraftDto details)
        {
            var months = details.SessionMonth switch
            {
                3 => "Feb Mar",
                6 => "May Jun",
                11 => "Oct Nov",
                _ => null,
            };
            var when = string.Join(' ', new[] { months, details.Year?.ToString(CultureInfo.InvariantCulture) }.Where(t => t is not null));
            return when.Length == 0 ? null : when;
        }

        /// <summary>
        /// The exam's title: the one set, or the default "subject component session year Online Exam", e.g.
        /// "Biology 5090-11 Oct Nov 2025 Online Exam" (the paper instead of the component when there is none).
        /// </summary>
        public static string Title(ExamImportDraftDto details)
        {
            if (!string.IsNullOrWhiteSpace(details.Title))
            {
                return details.Title;
            }

            // gama-api's subject names carry the syllabus code ("Physics (5054)"), which the component repeats.
            var subject = details.Component is null ? details.Subject : SubjectCodeRegex().Replace(details.Subject ?? string.Empty, string.Empty);
            var parts = new[] { subject, details.Component?.Replace('/', '-'), details.Component is null ? details.Paper : null, ShortSession(details), "Online Exam" };
            return string.Join(' ', parts.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!.Trim()));
        }

        /// <summary>Where a question comes from, stored with it on gama-api (<c>examTests.resource</c>): the board and the
        /// exam's title (subject, component, session), and the question's number.</summary>
        public static string ResourceLabel(ExamImportDraftDto draft, string number)
        {
            var head = string.Join(' ', new[] { draft.Board, draft.Title }.Where(t => !string.IsNullOrWhiteSpace(t)));
            var label = head.Length == 0 ? $"Q{number}" : $"{head}, Q{number}";
            return label.Length > 250 ? label[..250] : label;
        }

        /// <summary>
        /// What gama-api stores for a question on <paramref name="draft"/>: its HTML, the correct option, the answer and the
        /// images (gama-api upload keys; one left out keeps the image of a question being changed).
        /// </summary>
        public static SaveExamTestRequestDto ExamTestRequest(string token, ExamImportQuestionDto question, ExamImportDraftDto draft)
        {
            var need = OptionCount(question.Type);
            var imageOptions = need > 0 && question.OptionFigures?.Count > 0;
            var answer = ExamImportText.ToHtml(question.Answer);
            var options = question switch
            {
                _ when need == 0 || imageOptions => [],
                { Options.Count: > 0 } => question.Options,
                { Type: "tf" } => ["True", "False"],
                _ => [],
            };
            return new()
            {
                SecretKey = token,
                Id = question.Id,
                BoardId = draft.BoardId,
                GradeId = draft.GradeId,
                CourseId = draft.CourseId,
                SubjectId = draft.SubjectId,
                TopicId = question.TopicId,
                Level = question.Level,
                Type = question.Type,
                QuestionHtml = question.Text is null ? "<p>Refer to the figure.</p>" : ExamImportText.ToHtml(question.Text),
                OptionsHtml = [.. options.Take(need).Select(ExamImportText.ToHtml)],
                CorrectOption = need > 0 ? Letters.IndexOf(question.Correct ?? "A", StringComparison.Ordinal) + 1 : null,
                // gama-api requires answer_full text for the open types even when an answer image is attached.
                AnswerHtml = answer.Length == 0 && question.AnswerFigure is not null ? "<p>See the answer image.</p>" : answer,
                Resource = ResourceLabel(draft, question.Number),
                ImageOptions = imageOptions,
                QuestionFile = question.Figure,
                OptionFiles = imageOptions ? [.. question.OptionFigures!.Take(need)] : [],
                AnswerFile = question.AnswerFigure,
            };
        }

        private static string? NormalizeCorrect(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var letter = value.Trim().ToUpperInvariant().TrimEnd('.', ')');
            return CorrectAliases.GetValueOrDefault(letter, letter);
        }

        /// <summary>Whether the options are just their own letters in order ("A", "(B)", "C.", "Graph D"...), the choices
        /// being elsewhere.</summary>
        private static bool LettersOnly(IReadOnlyList<string> options) =>
            options.Select((t, i) => LetterOptionRegex().Match(t) is { Success: true } m && string.Equals(m.Groups["letter"].Value, Letter(i), StringComparison.OrdinalIgnoreCase)).All(t => t);

        private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static List<string>? NonEmpty(IReadOnlyList<string>? values) => values?.Select(t => t?.Trim()).OfType<string>().Where(t => t.Length > 0).ToList();

        /// <summary>Words that usually mean the question relies on a picture or table.</summary>
        [GeneratedRegex(@"\b(diagram|figure|fig\.|graph|table|chart|shown (below|above|opposite)|the (map|photograph|image)|insert|resource booklet)\b", RegexOptions.IgnoreCase)]
        private static partial Regex AssetHintRegex();

        /// <summary>An option that only names its letter: "A", "(A)", "A.", "**A**", "Graph A", "Diagram (A)"...</summary>
        [GeneratedRegex(@"^\s*(?:(?:graph|diagram|figure|fig\.|picture|drawing|circuit|chart|option|choice)\s*)?[(*_]*\s*(?<letter>[A-D])\s*[).*_]*\s*$", RegexOptions.IgnoreCase)]
        private static partial Regex LetterOptionRegex();

        /// <summary>A trailing code in brackets, e.g. " (5054)".</summary>
        [GeneratedRegex(@"\s*\([^()]*\)\s*$")]
        private static partial Regex SubjectCodeRegex();
    }
}
