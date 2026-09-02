namespace GamaEdtech.Presentation.ViewModel.Nudge
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.Enumeration;

    public sealed class ManageNudgeTemplateRequestViewModel
    {
        /// <summary>Required on create; ignored on update (the route id wins - see NudgesController.UpdateNudgeTemplate).</summary>
        [Display]
        [JsonConverter(typeof(EnumerationConverter<NudgeType, byte>))]
        public NudgeType? NudgeType { get; set; }

        [Display]
        public string? Subject { get; set; }

        [Display]
        public string? Body { get; set; }

        [Display]
        public string? CtaLabel { get; set; }

        [Display]
        public string? CtaUrl { get; set; }

        [Display]
        public bool? IsActive { get; set; }
    }
}
