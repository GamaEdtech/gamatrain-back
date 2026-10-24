namespace GamaEdtech.Presentation.ViewModel.ExamAnalysis
{
    using GamaEdtech.Common.DataAnnotation;

    public sealed class SaveExamAnalysisResultRequestViewModel
    {
        [Display]
        [Required]
        public string? Result { get; set; }
    }
}
