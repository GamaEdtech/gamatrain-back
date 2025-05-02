namespace GamaEdtech.Presentation.Api.Controllers
{
    using Microsoft.AspNetCore.Mvc;
    using GamaEdtech.Application.Interface;
    using GamaEdtech.Presentation.Api.Models;
    using Asp.Versioning;
    using GamaEdtech.Data.Dto.FaqManager;
    using GamaEdtech.Domain.DataAccess.Responses.FaqResponses;
    using GamaEdtech.Common.Core.Extensions;

    [ApiVersion("1.0")]
    public class FaqController(IFaqManager faqManager) : BaseController
    {
        [HttpGet("[action]")]
        public virtual async Task<ActionResult<List<FaqResponse>>> GetFaqsWithDynamicFilter([FromQuery] GetFaqWithDynamicFilterDto getFAQWithDynamicFilterDTO, CancellationToken cancellationToken)
        {
            var result = await faqManager.GetFaqWithDynamicFilterAsync(getFAQWithDynamicFilterDTO, cancellationToken);
            return Ok(result);
        }

        [HttpPost("[action]")]
        public virtual async Task<ActionResult<FaqResponse>> CreateForum([FromForm] CreateForumDto createForumDTO, CancellationToken cancellationToken)
        {
            var result = await faqManager.AddForumAsync(createForumDTO, cancellationToken);
            return Ok(result);
        }

        [HttpPost("[action]")]
        public virtual async Task<ActionResult> CreateFaqCategory(CreateFaqCategoryDto createFAQCategoryDTO, CancellationToken cancellationToken)
        {
            await faqManager.CreateFaqCategoryAsync(createFAQCategoryDTO, cancellationToken);
            return Ok();
        }

        [HttpGet("[action]")]
        public virtual async Task<ActionResult<List<ClassificationNodeResponse>>> GetFaqCategoryHierarchy([FromQuery] CustomDateFormat customDateFormat = CustomDateFormat.ToSolarDate, CancellationToken cancellationToken = default)
        {
            var result = await faqManager.GetFaqCategoryHierarchyAsync(customDateFormat, cancellationToken);
            return Ok(result);
        }
    }
}
