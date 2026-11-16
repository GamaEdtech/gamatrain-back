namespace GamaEdtech.Presentation.ViewModel.PastPaper
{
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>A review of a past paper: its new status and what the teacher should change.</summary>
    public sealed class ReviewPastPaperRequestViewModel
    {
        /// <summary><c>PastPaperStatus</c>: 0 waiting for review, 1 confirmed, 2 rejected, 3 needs changes, 4 deactivated.</summary>
        [Display]
        [Required]
        public byte? Status { get; set; }

        /// <summary>Notes for the teacher, one per item; shown on their edit form. Required unless the paper is confirmed.</summary>
        [Display]
        public IReadOnlyList<string>? Notes { get; set; }
    }
}
