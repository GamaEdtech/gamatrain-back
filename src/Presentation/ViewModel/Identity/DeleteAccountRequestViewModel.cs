namespace GamaEdtech.Presentation.ViewModel.Identity
{
    using GamaEdtech.Common.DataAnnotation;

    public sealed class DeleteAccountRequestViewModel
    {
        /// <summary>The 6-digit code emailed by profiles/delete-code.</summary>
        [Display]
        [Required]
        public string? Code { get; set; }
    }
}
