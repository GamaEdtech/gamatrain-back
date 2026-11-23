namespace GamaEdtech.Data.Dto.ExamBuilder
{
    /// <summary>A question of the bank with its options and correct answer (teachers and admins only).</summary>
    public sealed class ExamBankQuestionDto
    {
        public long Id { get; set; }

        public string? Code { get; set; }

        /// <summary>fourchoice, twochoice, tf, descriptive, blank or shortanswer.</summary>
        public string? Type { get; set; }

        public string? QuestionHtml { get; set; }

        public IReadOnlyList<string?> OptionsHtml { get; set; } = [];

        /// <summary>The correct option, 1 to 4 (none for a written answer).</summary>
        public int? CorrectOption { get; set; }

        public int? Level { get; set; }

        /// <summary>Its topics, gama-api's "id+id".</summary>
        public string? Topics { get; set; }

        /// <summary>Whether the caller wrote it.</summary>
        public bool Owner { get; set; }

        /// <summary>0 waiting, 1 published, 2 personal use / rejected.</summary>
        public int Status { get; set; }
    }
}
