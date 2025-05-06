namespace GamaEdtech.Presentation.ViewModel.Transaction
{
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;

    public sealed class TransactionsRequestViewModel
    {
        [Display]
        public PagingDto? PagingDto { get; set; } = new() { PageFilter = new(), };

        [Display]
        public bool? IsDebit { get; set; }
    }
}
