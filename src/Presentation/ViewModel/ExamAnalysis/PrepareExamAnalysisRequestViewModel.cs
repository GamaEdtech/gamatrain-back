namespace GamaEdtech.Presentation.ViewModel.ExamAnalysis
{
    using GamaEdtech.Common.DataAnnotation;

    public sealed class PrepareExamAnalysisRequestViewModel
    {
        [Display]
        [Required]
        public long? ExamId { get; set; }

        [Display]
        [Required]
        [System.ComponentModel.DataAnnotations.MinLength(1)]
        [System.ComponentModel.DataAnnotations.MaxLength(200)]
        public IReadOnlyList<ExamAnalysisQuestionViewModel>? Questions { get; set; }
    }
}
