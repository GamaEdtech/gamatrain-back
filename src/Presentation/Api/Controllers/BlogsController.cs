namespace GamaEdtech.Presentation.Api.Controllers
{
    using System.Diagnostics.CodeAnalysis;
    using System.Text.Json;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Common.DataAccess.Specification.Impl;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Data.Dto.Blog;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Domain.Specification;
    using GamaEdtech.Domain.Specification.Post;
    using GamaEdtech.Presentation.ViewModel.Blog;
    using GamaEdtech.Presentation.ViewModel.Tag;

    using Microsoft.AspNetCore.Mvc;

    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    public class BlogsController(Lazy<ILogger<BlogsController>> logger, Lazy<IBlogService> blogService
        , Lazy<IContributionService> contributionService, Lazy<IFileService> fileService)
        : ApiControllerBase<BlogsController>(logger)
    {
        [HttpGet("posts"), Produces<ApiResponse<ListDataSource<PostsResponseViewModel>>>()]
        public async Task<IActionResult<ListDataSource<PostsResponseViewModel>>> GetPosts([NotNull, FromQuery] PostsRequestViewModel request)
        {
            try
            {
                ISpecification<Post>? specification = request.TagId.HasValue
                    ? new TagIncludedSpecification(request.TagId.Value)
                    : null;
                var result = await blogService.Value.GetPostsAsync(new ListRequestDto<Post>
                {
                    PagingDto = request.PagingDto,
                    Specification = specification,
                });
                return Ok<ListDataSource<PostsResponseViewModel>>(new(result.Errors)
                {
                    Data = result.Data.List is null ? new() : new()
                    {
                        List = result.Data.List.Select(t => new PostsResponseViewModel
                        {
                            Id = t.Id,
                            Title = t.Title,
                            Summary = t.Summary,
                            LikeCount = t.LikeCount,
                            DislikeCount = t.DislikeCount,
                            ImageUri = t.ImageUri,
                        }),
                        TotalRecordsCount = result.Data.TotalRecordsCount,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ListDataSource<PostsResponseViewModel>>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpGet("posts/{postId:long}"), Produces<ApiResponse<PostResponseViewModel>>()]
        public async Task<IActionResult<PostResponseViewModel>> GetPost([FromRoute] long postId)
        {
            try
            {
                var result = await blogService.Value.GetPostAsync(new IdEqualsSpecification<Post, long>(postId));

                return Ok<PostResponseViewModel>(new(result.Errors)
                {
                    Data = result.Data is null ? null : new()
                    {
                        Title = result.Data.Title,
                        Summary = result.Data.Summary,
                        Body = result.Data.Body,
                        ImageUri = result.Data.ImageUri,
                        LikeCount = result.Data.LikeCount,
                        DislikeCount = result.Data.DislikeCount,
                        CreationUser = result.Data.CreationUser,
                        Tags = result.Data.Tags?.Select(t => new TagResponseViewModel
                        {
                            Id = t.Id,
                            Icon = t.Icon,
                            Name = t.Name,
                            TagType = t.TagType,
                        }),
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<PostResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpDelete("posts/{postId:long}"), Produces<ApiResponse<bool>>()]
        [Permission(policy: null)]
        public async Task<IActionResult> RemovePost([FromRoute] long postId)
        {
            try
            {
                var isCreator = await blogService.Value.IsCreatorOfPostAsync(postId, User.UserId());
                if (!isCreator.Data)
                {
                    return Ok(new ApiResponse<bool> { Errors = [new() { Message = "Invalid Request" }] });
                }

                var result = await blogService.Value.RemovePostAsync(new IdEqualsSpecification<Post, long>(postId));
                return Ok(new ApiResponse<bool>(result.Errors) { Data = result.Data });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok(new ApiResponse<bool> { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpPatch("posts/{postId:long}/like"), Produces<ApiResponse<bool>>()]
        [Permission(policy: null)]
        public async Task<IActionResult<bool>> LikePost([FromRoute] long postId)
        {
            try
            {
                var result = await blogService.Value.LikePostAsync(new()
                {
                    PostId = postId,
                });
                return Ok<bool>(new(result.Errors)
                {
                    Data = result.Data,
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<bool>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpPatch("posts/{postId:long}/dislike"), Produces<ApiResponse<bool>>()]
        [Permission(policy: null)]
        public async Task<IActionResult<bool>> DislikePost([FromRoute] long postId)
        {
            try
            {
                var result = await blogService.Value.DislikePostAsync(new()
                {
                    PostId = postId,
                });
                return Ok<bool>(new(result.Errors)
                {
                    Data = result.Data,
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<bool>(new(new Error { Message = exc.Message }));
            }
        }

        #region Contributions

        [HttpGet("contributions"), Produces<ApiResponse<ListDataSource<PostContributionListResponseViewModel>>>()]
        [Permission(policy: null)]
        public async Task<IActionResult<ListDataSource<PostContributionListResponseViewModel>>> GetPostContributionList([NotNull, FromQuery] PostContributionListRequestViewModel request)
        {
            try
            {
                var result = await contributionService.Value.GetContributionsAsync(new ListRequestDto<Contribution>
                {
                    PagingDto = request.PagingDto,
                    Specification = new CreationUserIdEqualsSpecification<Contribution, ApplicationUser, int>(User.UserId())
                        .And(new CategoryTypeEqualsSpecification<Contribution>(CategoryType.Post)),
                });
                return Ok<ListDataSource<PostContributionListResponseViewModel>>(new(result.Errors)
                {
                    Data = result.Data.List is null ? new() : new()
                    {
                        List = result.Data.List.Select(t => new PostContributionListResponseViewModel
                        {
                            Id = t.Id,
                            Comment = t.Comment,
                            Status = t.Status,
                            CreationUser = t.CreationUser,
                            CreationDate = t.CreationDate,
                        }),
                        TotalRecordsCount = result.Data.TotalRecordsCount,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ListDataSource<PostContributionListResponseViewModel>>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpGet("contributions/{contributionId:long}"), Produces<ApiResponse<PostContributionResponseViewModel>>()]
        [Permission(policy: null)]
        public async Task<IActionResult<PostContributionResponseViewModel>> GetPostContribution([FromRoute] long contributionId)
        {
            try
            {
                var specification = new IdEqualsSpecification<Contribution, long>(contributionId)
                    .And(new CreationUserIdEqualsSpecification<Contribution, ApplicationUser, int>(User.UserId()))
                    .And(new CategoryTypeEqualsSpecification<Contribution>(CategoryType.Post));
                var result = await contributionService.Value.GetContributionAsync(specification);

                PostContributionResponseViewModel? viewModel = null;
                if (result.Data?.Data is not null)
                {
                    var dto = JsonSerializer.Deserialize<PostContributionDto>(result.Data.Data);
                    viewModel = dto is null ? null : MapFrom(dto);
                }

                return Ok<PostContributionResponseViewModel>(new(result.Errors)
                {
                    Data = viewModel,
                });

                PostContributionResponseViewModel MapFrom(PostContributionDto dto) => new()
                {
                    Title = dto.Title,
                    Summary = dto.Summary,
                    Body = dto.Body,
                    Tags = dto.Tags,
                    ImageUri = string.IsNullOrEmpty(dto.ImageId) ? null : fileService.Value.GetFileUri(dto.ImageId, ContainerType.Post).Data,
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<PostContributionResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpPost("contributions"), Produces<ApiResponse<ManagePostContributionResponseViewModel>>()]
        [Permission(policy: null)]
        public async Task<IActionResult<ManagePostContributionResponseViewModel>> CreatePostContribution([NotNull, FromForm] PostContributionViewModel request)
        {
            try
            {
                var dto = MapTo(request, null);
                var result = await blogService.Value.ManagePostContributionAsync(dto);

                return Ok<ManagePostContributionResponseViewModel>(new(result.Errors)
                {
                    Data = new() { Id = result.Data, },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ManagePostContributionResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpPut("contributions/{contributionId:long}"), Produces<ApiResponse<ManagePostContributionResponseViewModel>>()]
        [Permission(policy: null)]
        public async Task<IActionResult<ManagePostContributionResponseViewModel>> UpdatePostContribution([FromRoute] long contributionId, [NotNull, FromForm] PostContributionViewModel request)
        {
            try
            {
                var isCreator = await contributionService.Value.IsCreatorOfContributionAsync(contributionId, User.UserId());
                if (!isCreator.Data)
                {
                    return Ok<ManagePostContributionResponseViewModel>(new(new Error { Message = "InvalidRequest" }));
                }

                var dto = MapTo(request, contributionId);
                var result = await blogService.Value.ManagePostContributionAsync(dto);

                return Ok<ManagePostContributionResponseViewModel>(new(result.Errors)
                {
                    Data = new() { Id = result.Data, },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ManagePostContributionResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        #endregion

        private ManagePostContributionRequestDto MapTo(PostContributionViewModel request, long? contributionId) => new()
        {
            ContributionId = contributionId,
            UserId = User.UserId(),
            Title = request.Title,
            Summary = request.Summary,
            Body = request.Body,
            Image = request.Image,
            Tags = request.Tags,
        };
    }
}
