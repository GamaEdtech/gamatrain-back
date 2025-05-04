namespace GamaEdtech.Presentation.Api.Controllers
{
    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Data.Dto.FaqManager;
    using GamaEdtech.Domain.DataAccess.Responses.FaqResponses;
    using GamaEdtech.Presentation.Api.Models;

    using Microsoft.AspNetCore.Mvc;

    [ApiVersion("1.0")]
    public class ClassificationNode(IFaqManager faqManager) : BaseController
    {
        [HttpPost("[action]")]
        public virtual async Task<ActionResult> CreateFaqCategoryAsync(CreateFaqCategoryDto createFAQCategoryDTO, CancellationToken cancellationToken)
        {
            await faqManager.CreateFaqCategoryAsync(createFAQCategoryDTO, cancellationToken);
            return Ok();
        }

        [HttpGet("[action]")]
        public virtual async Task<ActionResult<List<ClassificationNodeResponse>>> GetFaqCategoryHierarchyAsync(CancellationToken cancellationToken = default)
        {
            var result = await faqManager.GetFaqCategoryHierarchyAsync(cancellationToken);
            return Ok(result);
        }
    }
}
