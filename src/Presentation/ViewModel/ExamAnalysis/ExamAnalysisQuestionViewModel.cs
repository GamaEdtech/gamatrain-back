namespace GamaEdtech.Presentation.ViewModel.ExamAnalysis
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.DataAnnotation;

    public sealed class ExamAnalysisQuestionViewModel
    {
        [Display]
        [Required]
        [System.ComponentModel.DataAnnotations.StringLength(20000, MinimumLength = 1)]
        public string? Question { get; set; }

        [Display]
        [Required]
        [System.ComponentModel.DataAnnotations.MinLength(1)]
        [System.ComponentModel.DataAnnotations.MaxLength(20)]
        public IReadOnlyList<string>? Answers { get; set; }

        [Display]
        [Required]
        [JsonPropertyName("user_answer")]
        public int? UserAnswer { get; set; }

        [Display]
        [Required]
        [JsonPropertyName("true_answer")]
        public int? TrueAnswer { get; set; }
    }
}
