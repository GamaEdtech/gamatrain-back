namespace GamaEdtech.Presentation.ViewModel.Content
{
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.Enumeration;

    public class ContentOwnerCommissionsListRequestViewModel
    {
        [Display]
        public PagingDto? PagingDto { get; set; } = new() { PageFilter = new(), };

        [Display]
        public DateTimeOffset? StartDate { get; set; }

        [Display]
        public DateTimeOffset? EndDate { get; set; }

        /// <summary>Optional: only downloads (ContentDownload) or only invite earnings (ReferralSubscription).</summary>
        [Display]
        public CommissionReason? Reason { get; set; }
    }
}
