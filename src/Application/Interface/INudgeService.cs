namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.Nudge;
    using GamaEdtech.Domain.Entity;

    /// <summary>
    /// The proactive/scheduled nudge system - see docs/business/notifications.md, "Nudge system". Deliberately
    /// separate from IIdentityService/IApplicationSettingsService: NudgeTemplate is a real admin-managed entity
    /// (not a flat ApplicationSettingsDto property), and this is its own domain, not a facet of identity.
    /// </summary>
    [Injectable]
    public interface INudgeService
    {
        Task<ResultData<ListDataSource<NudgeTemplateDto>>> GetNudgeTemplatesAsync(ListRequestDto<NudgeTemplate>? requestDto = null);
        Task<ResultData<NudgeTemplateDto>> GetNudgeTemplateAsync([NotNull] ISpecification<NudgeTemplate> specification);
        Task<ResultData<int>> ManageNudgeTemplateAsync([NotNull] ManageNudgeTemplateRequestDto requestDto);
        Task<ResultData<bool>> RemoveNudgeTemplateAsync([NotNull] ISpecification<NudgeTemplate> specification);

        /// <summary>
        /// The daily recurring job entry point (Startup.cs). For every NudgeType with an Active NudgeTemplate,
        /// finds users eligible for it (registered long enough ago, the underlying condition still true, and not
        /// already sent recently/too many times per UserNudgeLog), sends, and logs. Never throws to the caller -
        /// see docs/business/notifications.md for the exact eligibility/cooldown rules.
        /// </summary>
        Task<ResultData<bool>> EvaluateAndSendNudgesAsync();
    }
}
