namespace GamaEdtech.Presentation.ViewModel.Nudge
{
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;

    public sealed class NudgeTemplatesRequestViewModel
    {
        [Display]
        public PagingDto? PagingDto { get; set; }
    }
}
