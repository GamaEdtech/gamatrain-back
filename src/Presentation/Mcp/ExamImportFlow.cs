namespace GamaEdtech.Presentation.Mcp
{
    using System.Globalization;
    using System.Text.Encodings.Web;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Data.Dto.ExamImport;
    using GamaEdtech.Presentation.ViewModel.ExamImport;

    /// <summary>
    /// The steps of "gamatrain exams" and their wording, in one place so that every assistant shows the same. Each tool
    /// answer ends with the next step: an <see cref="Ask"/> (a question, its options in order and at most one field), which
    /// the assistant shows as it is and answers by doing the chosen option's <c>next</c> (a tool call, or its own work for
    /// the steps that need the AI: reading the paper, fixing a question), or a <c>next</c> alone, the assistant's own work.
    /// In a <c>next</c>, <c>&lt;key&gt;</c> is the chosen option's key and <c>&lt;value&gt;</c> the typed value. See
    /// docs/business/exams-and-content.md, "Exam import through the MCP connector".
    /// </summary>
    internal static class ExamImportFlow
    {
        public const int PapersPerPage = 15;

        public const int ExamsPerPage = 20;

        private const string ChosenKey = "<key>";
        private const string TypedValue = "<value>";

        /// <summary>A list longer than this also takes a typed name.</summary>
        private const int MaxPickOptions = 12;

        private const string ReadGuide = "Read get_import_guide if you haven't yet.";

        private const string Finish = "Say goodbye. Call no more tools.";

        private const string ReadCover = $"{ReadGuide} Read the cover of the question paper attached to the chat (board, grade, subject, paper, component, session, year, duration), find their ids (list_options) and the past paper (find_past_papers), then call set_exam_details with them and confirmed=false.";

        /// <summary>The JSON the assistant reads: the tool answers and the arguments in a <c>next</c>.</summary>
        public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>Home: every path starts and ends here.</summary>
        public static Ask Home(ExamImportStatusDto status)
        {
            List<AskOption> options = [];
            if (status.Draft is { } draft)
            {
                options.Add(new("draft", "Continue my draft", Call("open_review", ("examId", draft.Id)), $"{draft.Title} · {Plural(draft.QuestionIds.Count, "question")}"));
            }

            options.Add(new("file", "New exam from my own file", Ask: new("file", "Attach the question paper and the mark scheme to the chat, then choose.",
            [
                new("continue", "Continue", ReadCover, "Files attached"),
                new("noMarkScheme", "Continue without a mark scheme", $"{ReadCover} There is no mark scheme: you write every answer yourself."),
                new("back", "Back", Call("open_exams")),
            ])));
            if (status.Staff)
            {
                options.Add(new("directory", "New exam from the Gamatrain directory", Call("search_papers")));
            }

            options.Add(new("exams", "My exams", Call("list_my_exams")));
            options.Add(new("signOut", "Sign out", Call("sign_out")));
            return new("home", "What do you want to do?", options);
        }

        /// <summary>The Gamatrain directory (staff): a paper ID, or words of its title.</summary>
        public static Ask Directory() => new("directory", "Enter a paper ID, or search by title.",
            [
                new("latest", "Show the latest papers", Call("list_recent_papers", ("page", 1))),
                new("back", "Back", Call("open_exams")),
            ],
            new("Paper ID or title", Call("search_papers", ("query", TypedValue)), "43604, or 9709 paper 1 2024"));

        public static Ask SearchResults(string query, int page, ListDataSource<ExamImportPastPaperDto> found)
        {
            var total = found.TotalRecordsCount ?? 0;
            var question = total == 0 ? $"No paper on Gamatrain matches \"{query}\"." : $"Select a paper ({Plural(total, "match")} for \"{query}\").";
            return Papers(question, [.. found.List ?? []], page * PapersPerPage < total ? Call("search_papers", ("query", query), ("page", page + 1)) : null);
        }

        public static Ask LatestPapers(int page, IReadOnlyCollection<ExamImportPastPaperDto> papers) =>
            Papers(papers.Count == 0 ? "There are no more papers." : "Select a paper (the latest on Gamatrain).", papers,
                papers.Count == PapersPerPage ? Call("list_recent_papers", ("page", page + 1)) : null);

        /// <summary>After load_paper: the assistant reads the paper's files and cover, then has the details shown.</summary>
        public static string ReadPaper(ExamImportPastPaperDto paper) => $"{ReadGuide} Read every file of the paper now: with a shell, download it (curl -L -o <name> <url>) and open it; otherwise open the link; if you can't open links, ask the user to download the files and attach them to the chat. Read the duration and the component code from the question paper's cover, then call set_exam_details with the paper's details (pastPaperId={paper.Id}) and confirmed=false.";

        /// <summary>A paper that already has an online exam: asked before going on.</summary>
        public static Ask ExamLinked(ExamImportPastPaperDto paper) => new("examLinked", $"{paper.Title} already has an online exam on Gamatrain. Make another one from it?",
        [
            new("yes", "Yes, go on", ReadPaper(paper)),
            new("search", "Pick another paper", Call("search_papers")),
        ]);

        /// <summary>The exam details, filled in: create the draft (or save the details of draft <c>Id</c>), or change one.</summary>
        public static Ask Details(ExamImportDraftDto details)
        {
            var draft = details.Id > 0;
            var summary = string.Join(" · ", new[] { details.Board, details.Grade, details.Subject, details.Paper, details.Session, $"{details.DurationMinutes} min" }.Where(t => !string.IsNullOrWhiteSpace(t)));
            var linked = details.PastPaperId is { } paperId ? $", linked to past paper {paperId}" : string.Empty;
            return new("details", $"{summary}. Title: {details.Title}{linked}. {(draft ? "Save these details on the draft?" : "Create a draft?")}",
            [
                new("create", draft ? "Save the details" : "Create the draft", Same("confirmed=true"), draft ? null : "Students can't see it yet"),
                new("change", "Change a detail", Ask: new("detail", "Which detail?",
                [
                    new("board", "Board", Again("boardId, gradeId, courseId and subjectId")),
                    new("grade", "Grade", Again("gradeId and subjectId")),
                    new("subject", "Subject", Again("subjectId")),
                    new("other", "Paper, session, year or duration", Ask: new("otherDetail", "Which one?",
                    [
                        new("paper", "Paper", Again("paperId")),
                        new("session", "Session", Ask: new("session", "Which session?",
                            [new("3", "February/March"), new("6", "May/June"), new("11", "October/November")],
                            Next: Same($"sessionMonth={ChosenKey}, confirmed=false"))),
                        new("year", "Year", Ask: new("year", "Which year?", Field: new("Year", Same($"year={TypedValue}, confirmed=false"), "2024"))),
                        new("duration", "Duration", Again("durationMinutes")),
                    ])),
                ])),
                new("back", "Back", Call("open_exams")),
            ]);
        }

        /// <summary>What set_exam_details refused, as a question for the user when it is theirs to answer: a detail to pick
        /// from Gamatrain's list (<c>pick{Detail}</c>, the choices in <paramref name="value"/>), or the draft they already have.</summary>
        public static Ask? DetailIssue(string? code, object? value) => code switch
        {
            "pickBoard" => Pick("Which board?", "boardId", value),
            "pickGrade" => Pick("Which grade?", "gradeId", value),
            "pickCourse" => Pick("Which course?", "courseId", value),
            "pickSubject" => Pick("Which subject?", "subjectId", value),
            "pickPaper" => Pick("Which paper?", "paperId", value),
            "pickDuration" => new("durationMinutes", "How long is the exam, in minutes?", Field: new("Minutes", Same($"durationMinutes={TypedValue}, confirmed=false"), "90")),
            "existingDraft" when value is ExamImportDraftDto draft => new("existingDraft", $"You already have a draft on Gamatrain: {draft.Title} ({Plural(draft.QuestionIds.Count, "question")}). Gamatrain keeps one draft at a time.",
            [
                new("continue", "Continue that draft", Call("open_review", ("examId", draft.Id))),
                new("replace", "Discard it and create this one", $"Call discard_draft(examId={draft.Id}, confirmed=true), then set_exam_details with the same details and confirmed=true.", "Its questions are deleted"),
                new("back", "Back", Call("open_exams")),
            ]),
            _ => null,
        };

        /// <summary>After the details are saved: for a new draft the assistant reads the paper and saves its questions, then
        /// opens the review; a draft whose details changed goes to the review.</summary>
        public static string AfterDetails(long examId, bool created) => created ? $"The draft is created (examId {Invariant(examId)}). Read every page of the paper, then the mark scheme, following the guide's extraction rules, and save the questions with save_questions in paper order, a few pages at a time, one call after another. Show progress lines only (\"Saved 14 of 40 questions…\"). When every question is saved, call open_review as save_questions says; don't write your own summary."
            : $"The details are saved. Call {Call("open_review", ("examId", examId))}.";

        /// <summary>After each save: the review comes when the whole paper is saved.</summary>
        public static string AfterSave(long examId) => $"If more of the paper is left, save it the same way. When every question is saved, call open_review(examId={Invariant(examId)}, flagged=<every result row so far whose status isn't saved, as it came>). While fixing a question for the review or the preview, go back as its option said instead.";

        /// <summary>
        /// The review: how the draft stands and one question that needs a decision at a time. <paramref name="flagged"/> are
        /// the save results that weren't simply saved; an answer the AI wrote is expected where the mark scheme has none, so
        /// those are counted, not asked about one by one, and the count goes on to the next step (<paramref name="aiAnswers"/>,
        /// the ones counted before) without their rows.
        /// </summary>
        public static Ask Review(ExamImportDraftDto draft, IReadOnlyList<ExamImportReviewQuestionViewModel> flagged, int aiAnswers)
        {
            aiAnswers += flagged.Count(OnlyAiAnswer);
            List<ExamImportReviewQuestionViewModel> pending = [.. flagged.Where(t => t.Status is "review" or "blocked" or "failed" && !OnlyAiAnswer(t))];
            var saved = draft.QuestionIds.Count;
            var written = aiAnswers > 0 ? $" · {Plural(aiAnswers, "answer")} written by the AI" : string.Empty;
            if (pending.Count == 0)
            {
                return new("review", $"{Plural(saved, "question")} on the draft{written}. Nothing needs a decision.",
                [
                    new("preview", "Go to preview", Call("show_preview", ("examId", draft.Id))),
                    new("add", "Add more questions", $"{ReadGuide} Ask the user to attach the question paper (and the mark scheme) if they aren't in this chat. Save the questions that aren't on the draft yet (it has {Plural(saved, "question")}) with save_questions(examId={Invariant(draft.Id)}), then call open_review as save_questions says."),
                    new("home", "Home", Call("open_exams")),
                ]);
            }

            var current = pending[0];
            var reason = Reason(current);
            var back = Call("open_review", ("examId", draft.Id), ("aiAnswers", aiAnswers), ("flagged", pending.Skip(1)));
            List<AskOption> options =
            [
                new("fix", "Fix it", $"Fix question {current.Number} ({reason}) and save it again with save_questions{(current.Id is { } id ? $", with its id {Invariant(id)}" : string.Empty)}. Then call {back}, adding question {current.Number}'s new row to flagged if it still isn't simply saved.", "The assistant fixes it, then comes back here"),
            ];
            if (current.Id is not null)
            {
                options.Add(new("keep", "Keep as it is", back));
            }

            options.Add(new("drop", "Drop it", current.Id is { } dropId ? $"Call remove_question(examId={Invariant(draft.Id)}, questionId={Invariant(dropId)}), then call {back}." : back));
            if (pending.Exists(t => t.Issues?.Any(i => i.Code is "missingAnswer" or "missingCorrect") == true))
            {
                options.Add(new("aiAnswers", "Let the AI write missing answers", $"Solve yourself every question in this list that has no answer or no correct letter (answerSource ai) and save them with save_questions. Then call open_review(examId={Invariant(draft.Id)}, aiAnswers={Invariant(aiAnswers)}, flagged=<this list, each of those questions replaced by its new row, the ones now simply saved left out>): {Json(pending)}"));
            }

            options.Add(new("preview", "Go to preview", Call("show_preview", ("examId", draft.Id))));
            var counts = $"{Invariant(saved)} saved · {Invariant(pending.Count(t => t.Status == "review"))} need a look · {Invariant(pending.Count(t => t.Status != "review"))} not saved{written}";
            return new("review", $"{counts}. Q{current.Number}: {reason}", options);
        }

        /// <summary>The preview: every question as students will see it, then publish, change a question, keep it as a draft or discard it.</summary>
        public static Ask Preview(long examId) => new("preview", "Check the figures and formulas. How does it look?",
        [
            new("publish", "Publish", Call("publish_exam", ("examId", examId)), "Asks once more"),
            new("change", "Change a question", Ask: new("changeQuestion", "Which question, and what should change?",
                [new("text", "Text"), new("options", "Options or correct answer"), new("figure", "Figure")],
                new("Question number", Example: "7"),
                $"Change the {ChosenKey} of question {TypedValue} (its number in the preview) the way the user wants: ask what is wrong if they haven't said, and cut a figure again from the paper. Save it again with its id (save_questions), then call show_preview(examId={Invariant(examId)}).")),
            new("close", "Save and close", Call("open_exams"), "It stays a draft"),
            new("discard", "Discard the draft", Call("discard_draft", ("examId", examId)), "Asks once more"),
        ]);

        public static Ask Publish(long examId) => new("publish", "Publish this exam to students?",
        [
            new("yes", "Yes, publish", Call("publish_exam", ("examId", examId), ("confirmed", true))),
            new("notYet", "Not yet", Call("show_preview", ("examId", examId))),
        ]);

        public static Ask Published(Uri? examUrl) => new("published", $"The exam is live: {examUrl}",
        [
            new("another", "Import another paper", Call("open_exams")),
            new("finish", "Finish", Finish),
        ]);

        public static Ask Discard(long examId) => new("discard", "Delete this draft and the questions made for it? This can't be undone.",
        [
            new("yes", "Yes, delete it", Call("discard_draft", ("examId", examId), ("confirmed", true))),
            new("no", "No, keep it", Call("show_preview", ("examId", examId))),
        ]);

        public static Ask Discarded() => new("discarded", "The draft is deleted.",
        [
            new("home", "Home", Call("open_exams")),
            new("finish", "Finish", Finish),
        ]);

        /// <summary>My exams: which ones to list.</summary>
        public static Ask MyExams() => new("myExams", "Which exams?",
        [
            new("draft", "Drafts", Call("list_my_exams", ("status", "draft"))),
            new("published", "Published", Call("list_my_exams", ("status", "published"))),
            new("all", "All", Call("list_my_exams", ("status", "all"))),
            new("back", "Back", Call("open_exams")),
        ]);

        public static Ask Exams(string status, int page, ListDataSource<ExamImportDraftDto> exams)
        {
            List<ExamImportDraftDto> list = [.. exams.List ?? []];
            List<AskOption> options = [.. list.Select(t => new AskOption(Invariant(t.Id), t.Title ?? $"Exam {Invariant(t.Id)}", Call("list_my_exams", ("examId", t.Id)),
                string.Join(" · ", new[] { StatusLabel(t.Status), t.Subject, t.Paper }.Where(i => !string.IsNullOrWhiteSpace(i)))))];
            if (page * ExamsPerPage < exams.TotalRecordsCount)
            {
                options.Add(new("more", "Show more", Call("list_my_exams", ("status", status), ("page", page + 1))));
            }

            options.Add(new("back", "Back", Call("list_my_exams")));
            var question = (list.Count, page, status) switch
            {
                ( > 0, _, _) => "Pick an exam.",
                (_, > 1, _) => "There are no more.",
                (_, _, "draft") => "You have no drafts.",
                (_, _, "published") => "You have no published exams.",
                _ => "You have no exams yet.",
            };
            return new("exams", question, options);
        }

        /// <summary>One of the user's exams: open it, and for a draft also preview, continue or discard it.</summary>
        public static Ask Exam(ExamImportDraftDto exam)
        {
            var url = exam.DraftUrl ?? exam.ExamUrl;
            List<AskOption> options = [new("open", "Open on Gamatrain", $"Give the user this link: {url}. Then show this question again.", url?.AbsoluteUri)];
            if (exam.DraftUrl is not null)
            {
                options.Add(new("preview", "Preview", Call("show_preview", ("examId", exam.Id))));
                options.Add(new("continue", "Continue the draft", Call("open_review", ("examId", exam.Id))));
                options.Add(new("discard", "Discard the draft", Call("discard_draft", ("examId", exam.Id)), "Asks once more"));
            }

            options.Add(new("back", "Back", Call("list_my_exams")));
            return new("exam", $"{exam.Title} · {StatusLabel(exam.Status)}. What now?", options);
        }

        public static Ask SignOut(string? user) => new("signOut", $"{(string.IsNullOrEmpty(user) ? string.Empty : $"Signed in as {user}. ")}Sign out of Gamatrain in this assistant?",
        [
            new("yes", "Yes, sign out", Call("sign_out", ("confirmed", true))),
            new("cancel", "Cancel", Call("open_exams")),
        ]);

        private static Ask Papers(string question, IReadOnlyCollection<ExamImportPastPaperDto> papers, string? more)
        {
            List<AskOption> options = [.. papers.Select(t => new AskOption(Invariant(t.Id), $"{Invariant(t.Id)} · {t.Title}", Call("load_paper", ("paperId", t.Id)), PaperDetail(t)))];
            if (more is not null)
            {
                options.Add(new("more", "Show more", more));
            }

            options.Add(new("search", "Search again", Call("search_papers")));
            options.Add(new("back", "Back", Call("open_exams")));
            return new("papers", question, options);
        }

        private static string PaperDetail(ExamImportPastPaperDto paper)
        {
            var files = paper.Files ?? [];
            var questions = files.Any(t => t.Type is "pdf" or "word");
            var markScheme = files.Any(t => t.Type == "answer");
            var content = (questions, markScheme) switch
            {
                (true, true) => "paper + mark scheme",
                (true, false) => "paper only",
                (false, true) => "mark scheme only",
                _ => "no files",
            };
            return string.Join(" · ", new[] { paper.Classification, content, paper.ExamLinked == true ? "has an online exam" : null }.Where(t => !string.IsNullOrEmpty(t)));
        }

        /// <summary>A detail to pick from Gamatrain's list; a long list also takes a typed name.</summary>
        private static Ask Pick(string question, string detail, object? value)
        {
            List<ExamImportOptionDto> choices = [.. value as IEnumerable<ExamImportOptionDto> ?? []];
            return new(detail, question, [.. choices.Select(t => new AskOption(Invariant(t.Id), t.Title ?? Invariant(t.Id)))],
                choices.Count > MaxPickOptions ? new("Or type its name", Same($"{detail} = the key of the option above whose label matches {TypedValue} (if none or several match, show this question again), confirmed=false")) : null,
                Same($"{detail}={ChosenKey}, confirmed=false"));
        }

        private static string Same(string change) => $"Call set_exam_details with the same details and {change}.";

        private static string Again(string leftOut) => $"Call set_exam_details with the same details but without {leftOut}, and confirmed=false: the user then picks them.";

        private static bool OnlyAiAnswer(ExamImportReviewQuestionViewModel question) =>
            question.Status == "review" && question.Issues is { Count: > 0 } issues && issues.All(t => t.Code == "aiAnswer");

        private static string Reason(ExamImportReviewQuestionViewModel question)
        {
            var issues = string.Join(" ", (question.Issues ?? []).Where(t => t.Code != "aiAnswer").Select(t => t.Message).Where(t => !string.IsNullOrWhiteSpace(t)));
            return (question.Error, issues.Length, question.Status) switch
            {
                ({ } error, _, _) => error,
                (_, > 0, _) => issues,
                (_, _, "review") => "needs a look",
                _ => "not saved",
            };
        }

        private static string StatusLabel(int status) => status switch
        {
            6 => "draft",
            1 or 5 or 7 => "published",
            _ => "not published",
        };

        /// <summary>A tool call as the assistant should make it: <c>tool(name=value, ...)</c>, the values in JSON.</summary>
        private static string Call(string tool, params (string Name, object? Value)[] args) =>
            $"{tool}({string.Join(", ", args.Select(t => $"{t.Name}={Json(t.Value)}"))})";

        private static string Json(object? value) => JsonSerializer.Serialize(value, JsonOptions);

        private static string Invariant(long value) => value.ToString(CultureInfo.InvariantCulture);

        private static string Plural(int count, string noun) => $"{Invariant(count)} {noun}" + (count, noun[^1]) switch
        {
            (1, _) => string.Empty,
            (_, 'h') => "es",
            _ => "s",
        };

        /// <summary>A question for the user: show <see cref="Question"/> and the options word for word and in order, or
        /// take the <see cref="Field"/>'s one typed value; then do the chosen option's <c>next</c>, or <see cref="Next"/>
        /// when it has none.</summary>
        public sealed record Ask(string Id, string Question, IReadOnlyList<AskOption>? Options = null, AskField? Field = null, string? Next = null);

        /// <summary>A choice: picking it does <see cref="Next"/>, or shows <see cref="Ask"/> at once.</summary>
        public sealed record AskOption(string Key, string Label, string? Next = null, string? Detail = null, Ask? Ask = null);

        /// <summary>One typed value; its <see cref="Next"/>, or the ask's when it has none, takes it as <c>&lt;value&gt;</c>.</summary>
        public sealed record AskField(string Label, string? Next = null, string? Example = null);
    }
}
