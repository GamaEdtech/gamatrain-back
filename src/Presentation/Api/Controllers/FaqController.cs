namespace GamaEdtech.Presentation.Api.Controllers
{
    using Microsoft.AspNetCore.Mvc;
    using GamaEdtech.Application.Interface;
    using GamaEdtech.Presentation.Api.Models;
    using Asp.Versioning;
    using GamaEdtech.Data.Dto.FaqManager;
    using GamaEdtech.Domain.DataAccess.Responses.FaqResponses;
    using GamaEdtech.Data.Dto.ClassificationNodes;

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

        [HttpPost("[action]/{faqId:guid}")]
        public virtual async Task<ActionResult> AddFaqRelationShip([FromRoute] Guid faqId, [FromBody] AddClassificationNode addClassificationNode, CancellationToken cancellationToken)
        {
            await faqManager.AddFaqRelationShipAsync(addClassificationNode, faqId, cancellationToken).ConfigureAwait(false);
            return Ok();
        }
    }
}
