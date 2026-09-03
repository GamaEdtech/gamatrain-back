namespace GamaEdtech.Data.Dto.Nudge
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class ManageNudgeTemplateRequestDto
    {
        public int? Id { get; set; }
        public NudgeType? NudgeType { get; set; }
        public string? Subject { get; set; }
        public string? Body { get; set; }
        public string? CtaLabel { get; set; }
        public string? CtaUrl { get; set; }
        public bool? IsActive { get; set; }
    }
}
