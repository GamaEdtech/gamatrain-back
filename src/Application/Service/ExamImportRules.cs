namespace GamaEdtech.Application.Service
{
    using System.Globalization;
    using System.Text.RegularExpressions;

    using GamaEdtech.Data.Dto.ExamImport;

    using static GamaEdtech.Data.Dto.ExamImport.ExamImportReportDto;

    /// <summary>
    /// What an imported question must meet before it goes to gama-api (the same required fields as gama-api's
    /// <c>Examtest_lib::checkRequiredFields</c>, plus what a human should look at), and the gama-api request bodies built
    /// for a draft exam, mirroring what gamatrain's test-maker sends. Pure functions; <see cref="ExamImportService"/> calls.
    /// </summary>
    internal static partial class ExamImportRules
    {
        /// <summary>gama-api's <c>staticValues('examMinTestsNum')</c> on gamatrain (LANG=en).</summary>
        public const int MinQuestionsToPublish = 1;

        public const string SavedStatus = "saved";
        public const string ReviewStatus = "review";
        public const string BlockedStatus = "blocked";
        public const string SkippedStatus = "skipped";
        public const string RemovedStatus = "removed";
        public const string FailedStatus = "failed";

        private const string Letters = "ABCD";
        private const string ErrorSeverity = "error";
        private const string ReviewSeverity = "review";
        private const int MaxTextLength = 5000;

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
        private static readonly Dictionary<int, string> SessionMonths = new() { [3] = "February/March", [6] = "May/June", [11] = "October/November" };

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

                var validLetters = Letters[..need];
                if (question.Correct is null)
                {
                    Error("missingCorrect", "No correct answer (no mark scheme entry). Add it or skip the question.");
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
                    Error("missingAnswer", $"{question.Type} questions need a model answer (mark scheme). Add it or skip the question.");
                }
            }

            if (question.AnswerSource == "ai")
            {
                Review("aiAnswer", "The answer was written by the AI, not taken from the mark scheme.");
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

        public static string? SessionLabel(ExamImportDraftDto details)
        {
            var when = string.Join(' ', new[] { SessionMonths.GetValueOrDefault(details.SessionMonth ?? 0), details.Year?.ToString(CultureInfo.InvariantCulture) }.Where(t => !string.IsNullOrEmpty(t)));
            return when.Length == 0 ? null : when;
        }

        /// <summary>The exam's title: the one set, or subject + component (or paper) + session.</summary>
        public static string Title(ExamImportDraftDto details)
        {
            if (!string.IsNullOrWhiteSpace(details.Title))
            {
                return details.Title;
            }

            var title = string.Join(' ', new[] { details.Subject, details.Component, details.Component is null ? details.Paper : null }
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t!.Trim()));
            title = title.Length == 0 ? "Online exam" : title;
            return SessionLabel(details) is { } when ? $"{title} — {when}" : title;
        }

        /// <summary>Where a question comes from, stored with it on gama-api (<c>examTests.resource</c>): the board and the
        /// exam's title (subject, component, session), and the question's number.</summary>
        public static string ResourceLabel(ExamImportDraftDto draft, string number)
        {
            var head = string.Join(' ', new[] { draft.Board, draft.Title }.Where(t => !string.IsNullOrWhiteSpace(t)));
            var label = head.Length == 0 ? $"Q{number}" : $"{head}, Q{number}";
            return label.Length > 250 ? label[..250] : label;
        }

        /// <summary><c>POST exams</c> / <c>PUT exams/{id}</c> form.</summary>
        public static List<KeyValuePair<string, string?>> ExamForm(ExamImportDraftDto details)
        {
            List<KeyValuePair<string, string?>> form =
            [
                new("section", Invariant(details.BoardId)),
                new("base", Invariant(details.GradeId)),
                new("lesson", Invariant(details.SubjectId)),
                new("exam_type", Invariant(details.PaperId)),
                new("duration", Invariant(details.DurationMinutes)),
                new("title", SafeTitle(Title(details))),
                new("negative_point", details.NegativeMarking ? "1" : "0"),
            ];
            AddIfSet(form, "course", details.CourseId);
            AddIfSet(form, "level", details.Level);
            AddIfSet(form, "edu_year", details.Year);
            AddIfSet(form, "edu_month", details.SessionMonth);
            AddIfSet(form, "paperID", details.PastPaperId);
            return form;
        }

        /// <summary>
        /// <c>POST examTests</c> / <c>PUT examTests/{id}</c> form for a question on <paramref name="draft"/>. Its figures are
        /// gama-api upload keys, sent as <c>q_file</c>, <c>a_file</c>..<c>d_file</c> and <c>answer_full_file</c>; a figure
        /// left out keeps the image of a question being changed.
        /// </summary>
        public static List<KeyValuePair<string, string?>> QuestionForm(ExamImportQuestionDto question, ExamImportDraftDto draft)
        {
            var need = OptionCount(question.Type);
            var imageOptions = need > 0 && question.OptionFigures?.Count > 0;
            var answer = ExamImportText.ToHtml(question.Answer);
            List<KeyValuePair<string, string?>> form =
            [
                new("section", Invariant(draft.BoardId)),
                new("base", Invariant(draft.GradeId)),
                new("lesson", Invariant(draft.SubjectId)),
                new("type", question.Type),
                new("direction", "ltr"),
                new("question", question.Text is null ? "<p>Refer to the figure.</p>" : ExamImportText.ToHtml(question.Text)),
                // gama-api requires answer_full text for the open types even when an answer image is attached.
                new("answer_full", answer.Length == 0 && question.AnswerFigure is not null ? "<p>See the answer image.</p>" : answer),
                new("resource", ResourceLabel(draft, question.Number)),
                new("testImgAnswers", imageOptions ? "1" : "0"),
            ];
            AddIfSet(form, "course", draft.CourseId);
            AddIfSet(form, "topic", question.TopicId);
            AddIfSet(form, "level", question.Level);

            if (need > 0)
            {
                form.Add(new("true_answer", Invariant(Letters.IndexOf(question.Correct ?? "A", StringComparison.Ordinal) + 1)));
                if (!imageOptions)
                {
                    var options = question switch
                    {
                        { Options.Count: > 0 } => question.Options,
                        { Type: "tf" } => ["True", "False"],
                        _ => [],
                    };
                    for (var i = 0; i < need && i < options.Count; i++)
                    {
                        form.Add(new($"answer_{char.ToLowerInvariant(Letters[i])}", ExamImportText.ToHtml(options[i])));
                    }
                }
            }

            AddFile(form, "q_file", question.Figure);
            if (imageOptions)
            {
                for (var i = 0; i < need && i < question.OptionFigures!.Count; i++)
                {
                    AddFile(form, $"{char.ToLowerInvariant(Letters[i])}_file", question.OptionFigures[i]);
                }
            }

            AddFile(form, "answer_full_file", question.AnswerFigure);
            return form;

            static void AddFile(List<KeyValuePair<string, string?>> form, string name, string? key)
            {
                if (key is not null)
                {
                    form.Add(new(name, key));
                }
            }
        }

        /// <summary>gama-api's exams endpoint strips <c>" ' ! = * #</c>, <c>/</c> and newlines from titles: replace <c>/</c>
        /// instead of losing it.</summary>
        public static string SafeTitle(string title)
        {
            var safe = WhitespaceRegex().Replace(TitleStripRegex().Replace(title.Replace('/', '-'), string.Empty), " ").Trim();
            return safe.Length > 200 ? safe[..200] : safe;
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

        private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static List<string>? NonEmpty(IReadOnlyList<string>? values) => values?.Select(t => t?.Trim()).OfType<string>().Where(t => t.Length > 0).ToList();

        private static string? Invariant(long? value) => value?.ToString(CultureInfo.InvariantCulture);

        private static void AddIfSet(List<KeyValuePair<string, string?>> form, string name, long? value)
        {
            if (value is > 0)
            {
                form.Add(new(name, Invariant(value)));
            }
        }

        /// <summary>Words that usually mean the question relies on a picture or table.</summary>
        [GeneratedRegex(@"\b(diagram|figure|fig\.|graph|table|chart|shown (below|above|opposite)|the (map|photograph|image)|insert|resource booklet)\b", RegexOptions.IgnoreCase)]
        private static partial Regex AssetHintRegex();

        [GeneratedRegex("[\"'!=*#\\n\\r\\t]")]
        private static partial Regex TitleStripRegex();

        [GeneratedRegex(@"\s+")]
        private static partial Regex WhitespaceRegex();
    }
}
