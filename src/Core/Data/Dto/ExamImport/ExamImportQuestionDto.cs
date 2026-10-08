namespace GamaEdtech.Data.Dto.ExamImport
{
    using System.ComponentModel;
    using System.ComponentModel.DataAnnotations;

    /// <summary>
    /// One question exactly as the AI extracted it from the paper: the <c>save_questions</c> MCP tool's input and the
    /// stored shape (<c>ExamImport.Questions</c>). The descriptions are what the AI reads in the tool's schema.
    /// </summary>
    public sealed class ExamImportQuestionDto
    {
        [Description("The question's label in the paper, unique in this import, e.g. \"7\" or \"3(b)(ii)\".")]
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

        [Description("The model answer or the marking points, same markup as text. Required for descriptive, shortanswer and blank.")]
        public string? Answer { get; set; }

        [Description("Where correct/answer came from. Answers written by the AI (ai) always stay flagged for review.")]
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

        [Description("Figure id (from add_figure) shown with the question. One image per question: combine several figures into one image first.")]
        public string? Figure { get; set; }

        [Description("Figure ids for image options, in order A to D.")]
        public IReadOnlyList<string>? OptionFigures { get; set; }

        [Description("Figure id for the answer image (a drawing or graph in the mark scheme).")]
        public string? AnswerFigure { get; set; }

        [Description("Anything uncertain (an unclear word, a guessed answer, a possibly incomplete figure). Flags the question for review.")]
        public IReadOnlyList<string>? ReviewNotes { get; set; }

        [Description("True keeps the question in the list but does not upload it.")]
        public bool Skip { get; set; }
    }
}
