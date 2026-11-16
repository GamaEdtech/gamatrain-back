namespace GamaEdtech.Presentation.Api.Areas.Admin.Controllers
{
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Data.Dto.Taxonomy;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Presentation.ViewModel.Taxonomy;

    using Microsoft.AspNetCore.Mvc;

    /// <summary>
    /// The admin panel's lookup lists from this backend's copy (boards, grades, subjects, courses, classifications, topics,
    /// papers, extra file types, variants). Changes are refused (<c>notNative</c>) while <c>Migration:WriteRoutes:Types</c> is
    /// not <c>Native</c>. See docs/architecture/legacy-migration.md, "Types".
    /// </summary>
    [Common.DataAnnotation.Area(nameof(Admin), "Admin")]
    [Route("api/v{version:apiVersion}/[area]/[controller]")]
    [ApiVersion("1.0")]
    [Permission(Roles = [nameof(Role.Admin)])]
    [DelegablePermission]
    public class TypesController(Lazy<ILogger<TypesController>> logger, Lazy<ILegacyTaxonomyAdminService> taxonomyAdminService)
        : ApiControllerBase<TypesController>(logger)
    {
        /// <summary>A list's items in list order: <c>kind</c> (required), <c>parent</c> (one parent key: a board id, a grade id,
        /// "course,grade" for subjects...), <c>title</c>, <c>includeDeleted</c>, <c>page</c>, <c>pageSize</c> (max 500).</summary>
        [HttpGet(""), Produces<ApiResponse<TaxonomyItemListDto>>()]
        public Task<IActionResult<TaxonomyItemListDto>> GetItems([FromQuery] short kind, [FromQuery] string? parent, [FromQuery] string? title,
            [FromQuery] bool includeDeleted = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 100) =>
            AnswerAsync(() => taxonomyAdminService.Value.GetItemsAsync(kind, parent, title, includeDeleted, page, pageSize));

        [HttpGet("{id:int}"), Produces<ApiResponse<TaxonomyItemDto>>()]
        public Task<IActionResult<TaxonomyItemDto>> GetItem([FromRoute] int id) =>
            AnswerAsync(() => taxonomyAdminService.Value.GetItemAsync(id));

        /// <summary>Adds an item under the next id; answers its id.</summary>
        [HttpPost(""), Produces<ApiResponse<int>>()]
        public Task<IActionResult<int>> CreateItem([NotNull, FromBody] TypeItemRequestViewModel request) =>
            AnswerAsync(() => taxonomyAdminService.Value.CreateItemAsync(ToDto(request)));

        /// <summary>Edits an item (it stays in its list).</summary>
        [HttpPut("{id:int}"), Produces<ApiResponse<bool>>()]
        public Task<IActionResult<bool>> UpdateItem([FromRoute] int id, [NotNull, FromBody] TypeItemRequestViewModel request) =>
            AnswerAsync(() => taxonomyAdminService.Value.UpdateItemAsync(id, ToDto(request)));

        /// <summary>Hides an item, as gama-api's delete; papers that use it keep it.</summary>
        [HttpDelete("{id:int}"), Produces<ApiResponse<bool>>()]
        public Task<IActionResult<bool>> RemoveItem([FromRoute] int id) =>
            AnswerAsync(() => taxonomyAdminService.Value.RemoveItemAsync(id));

        private static TaxonomyItemEditDto ToDto(TypeItemRequestViewModel request) => new()
        {
            Kind = request.Kind.GetValueOrDefault(),
            Title = request.Title,
            ListOrder = request.ListOrder,
            Parent = request.Parent,
            Parent2 = request.Parent2,
            Metadata = request.Metadata,
        };

        private async Task<IActionResult<T>> AnswerAsync<T>(Func<Task<ResultData<T>>> call)
        {
            try
            {
                var result = await call();
                return Ok<T>(new(result.Errors) { Data = result.Data });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<T>(new(new Error { Message = exc.Message }));
            }
        }
    }
}
