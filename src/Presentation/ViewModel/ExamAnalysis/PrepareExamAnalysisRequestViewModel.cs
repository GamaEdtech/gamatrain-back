namespace GamaEdtech.Presentation.ViewModel.ExamAnalysis
{
    using GamaEdtech.Common.DataAnnotation;

    public sealed class PrepareExamAnalysisRequestViewModel
    {
        [Display]
        [Required]
        public long? ExamId { get; set; }
    }
}
