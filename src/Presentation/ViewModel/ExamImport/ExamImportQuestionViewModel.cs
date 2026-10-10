namespace GamaEdtech.Presentation.ViewModel.ExamImport
{
    using System.ComponentModel;
    using System.ComponentModel.DataAnnotations;

    /// <summary>
    /// One question exactly as the AI extracted it from the paper: the <c>save_questions</c> MCP tool's public input. The
    /// descriptions are what the AI reads in the tool's schema; the tool maps it to <c>ExamImportQuestionDto</c>.
    /// </summary>
    public sealed class ExamImportQuestionViewModel
    {
        [Description("The question's id on Gamatrain, from an earlier save_questions result: to change that question. Leave it out for a new question.")]
        public long? Id { get; set; }

        [Description("The question's label in the paper, e.g. \"7\" or \"3(b)(ii)\".")]
        public required string Number { get; set; }

        [Description("fourchoice (4 lettered choices), twochoice (2 choices), tf (true/false), descriptive (explain, show, calculate with working), shortanswer (one word, number or phrase) or blank (fill a gap).")]
        [AllowedValues("fourchoice", "twochoice", "tf", "descriptive", "shortanswer", "blank")]
        public required string Type { get; set; }

        [Description("Self-contained question text, including the shared stem of a multi-part question. Plain text: a blank line starts a paragraph, **bold**, __underline__, math as TeX in $...$ or $$...$$.")]
        public string? Text { get; set; }

        [Description("Choice texts in order A, B, C, D: fourchoice 4, twochoice 2, tf 2 (e.g. True, False). Leave empty when the options are images (optionFigures).")]
        public IReadOnlyList<string>? Options { get; set; }

        [Description("The correct choice letter, A to D (tf: A is the first option).")]
        public string? Correct { get; set; }

        [Description("The worked solution (Gamatrain's descriptive answer), same markup as text, ending with the final answer. Required for every question, multiple choice included: when the mark scheme has none, write it yourself without asking.")]
        public string? Answer { get; set; }

        [Description("Where correct/answer came from: markScheme also when you wrote the explanation around the mark scheme's answer; ai when you solved the question yourself (always flagged for review).")]
        [AllowedValues("markScheme", "paper", "ai", "user")]
        public string? AnswerSource { get; set; }

        [Description("The marks printed for the question.")]
        [Range(0, 100)]
        public int? Marks { get; set; }

        [Description("Gamatrain topic id, from the topics set_exam_details returns.")]
        public int? TopicId { get; set; }

        [Description("1 easy, 2 medium, 3 hard. Only when the paper or the user says so.")]
        [Range(1, 3)]
        public int? Level { get; set; }

        [Description("True when the question relies on a diagram, graph, picture or table printed in the paper.")]
        public bool NeedsFigure { get; set; }

        [Description("The figure key from add_figure, shown with the question. A key works for one question only. One image per question: combine several figures into one image first. When changing a question, leave it out to keep its image.")]
        public string? Figure { get; set; }

        [Description("Figure keys (from add_figure) for image options, in order A to D. When changing such a question, give them again (new keys).")]
        public IReadOnlyList<string>? OptionFigures { get; set; }

        [Description("The figure key (from add_figure) for the answer image: a drawing or graph from the mark scheme, or one you drew for the worked solution. When changing a question, leave it out to keep its image.")]
        public string? AnswerFigure { get; set; }

        [Description("Anything uncertain (an unclear word, a guessed answer, a possibly incomplete figure). Flags the question for review.")]
        public IReadOnlyList<string>? ReviewNotes { get; set; }

        [Description("True leaves the question out of the exam: it is not saved, and with an id it is removed from the draft.")]
        public bool Skip { get; set; }
    }
}
