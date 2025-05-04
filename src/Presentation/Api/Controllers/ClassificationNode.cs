namespace GamaEdtech.Presentation.Api.Controllers
{
    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Data.Dto.ClassificationNodes;
    using GamaEdtech.Domain.DataAccess.Responses.FaqResponses;
    using GamaEdtech.Presentation.Api.Models;

    using Microsoft.AspNetCore.Mvc;

    [ApiVersion("1.0")]
    public class ClassificationNode(IClassificationNodeService classificationNodeService) : BaseController
    {
        [HttpPost("[action]")]
        public virtual async Task<ActionResult> CreateClassificationAsync(CreateClassificationNodeDto createFAQCategoryDTO, CancellationToken cancellationToken)
        {
            await classificationNodeService.CreateClassificationNodeAsync(createFAQCategoryDTO, cancellationToken);
            return Ok();
        }

        [HttpGet("[action]")]
        public virtual async Task<ActionResult<List<ClassificationNodeResponse>>> GetClassificationHierarchyAsync(CancellationToken cancellationToken = default)
        {
            var result = await classificationNodeService.GetClassificationNodesHierarchyAsync(cancellationToken);
            return Ok(result);
        }
    }
}
