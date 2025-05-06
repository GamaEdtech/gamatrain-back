namespace GamaEdtech.Presentation.ViewModel.Question
{
    using GamaEdtech.Common.DataAnnotation;

    public sealed class ManageQuestionRequestViewModel
    {
        [Display]
        [Required]
        public string? Body { get; set; }

        [Display]
        [Required]
        public IEnumerable<OptionViewModel> Options { get; set; }
    }
}
