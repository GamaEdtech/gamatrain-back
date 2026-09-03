namespace GamaEdtech.Presentation.ViewModel.Nudge
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Domain.Enumeration;

    public sealed class NudgeTemplateResponseViewModel
    {
        public int Id { get; set; }

        [JsonConverter(typeof(EnumerationConverter<NudgeType, byte>))]
        public NudgeType? NudgeType { get; set; }

        public string? Subject { get; set; }

        public string? Body { get; set; }

        public string? CtaLabel { get; set; }

        public string? CtaUrl { get; set; }

        public bool IsActive { get; set; }
    }
}
