namespace GamaEdtech.Presentation.ViewModel.Question
{
    using GamaEdtech.Common.DataAnnotation;

    public sealed class RandomQuestionsRequestViewModel
    {
        [Display]
        [Required]
        public int? Count { get; set; }
    }
}
