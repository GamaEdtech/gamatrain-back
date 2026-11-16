namespace GamaEdtech.Presentation.ViewModel.PastPaper
{
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>A past paper's referee score: 1 to 5 (D, C, B, A, A+), 0 clears it.</summary>
    public sealed class PastPaperScoreRequestViewModel
    {
        [Display]
        [Required]
        [System.ComponentModel.DataAnnotations.Range(0, 5)]
        public byte? Score { get; set; }
    }
}
