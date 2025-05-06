namespace GamaEdtech.Presentation.ViewModel.Transaction
{
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.Enumeration;

    public sealed class TransactionStatisticsRequestViewModel
    {
        [Display]
        public DateOnly? StartDate { get; set; }

        [Display]
        public DateOnly? EndDate { get; set; }

        [Display]
        [Required]
        public Period Period { get; set; }
    }
}
