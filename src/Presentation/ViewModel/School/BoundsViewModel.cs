namespace GamaEdtech.Presentation.ViewModel.School
{
    using GamaEdtech.Common.DataAnnotation;

    public sealed class BoundsViewModel
    {
        [Display]
        [Required]
        public double? South { get; set; }

        [Display]
        [Required]
        public double? West { get; set; }

        [Display]
        [Required]
        public double? North { get; set; }

        [Display]
        [Required]
        public double? East { get; set; }
    }
}
