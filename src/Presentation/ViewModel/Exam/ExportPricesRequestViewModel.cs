namespace GamaEdtech.Presentation.ViewModel.Exam
{
    using GamaEdtech.Common.DataAnnotation;

    public sealed class ExportPricesRequestViewModel
    {
        [Display]
        [Required]
        public long? Id { get; set; }
    }
}
