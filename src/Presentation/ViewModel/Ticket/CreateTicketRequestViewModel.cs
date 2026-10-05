namespace GamaEdtech.Presentation.ViewModel.Ticket
{
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.DataAnnotation;

    using Microsoft.AspNetCore.Http;

    public sealed class CreateTicketRequestViewModel
    {
        [Display]
        [Required]
        public string? Captcha { get; set; }

        [Display]
        [Required]
        public string? FullName { get; set; }

        /// <summary>Required for anonymous callers only; a signed-in user's account email is used instead.</summary>
        [Display]
        [EmailAddress]
        public string? Email { get; set; }

        [Display]
        [Required]
        public string? Subject { get; set; }

        [Display]
        [Required]
        public string? Body { get; set; }

        [Display]
        [FileSize(1024 * 1024 * 2)]//2MB
        [FileExtensions(Constants.ValidWebExtensions + "," + Constants.ValidImageExtensions)]
        public IFormFile? File { get; set; }
    }
}
