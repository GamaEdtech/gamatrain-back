namespace GamaEdtech.Presentation.ViewModel.Content
{
    using GamaEdtech.Common.DataAnnotation;

    public sealed class AdminContentOwnerCommissionsListRequestViewModel : ContentOwnerCommissionsListRequestViewModel
    {
        /// <summary>Unlike the user-facing report, admins may look up any owner's commissions, or omit this to see every owner.</summary>
        [Display]
        public long? OwnerUserId { get; set; }
    }
}
