namespace GamaEdtech.Presentation.Api.Areas.Admin.Controllers
{
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Core.Extensions.Collections.Generic;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Common.DataAccess.Specification.Impl;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Data.Dto.Post;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Domain.Specification;
    using GamaEdtech.Domain.Specification.ApplicationSetting;
    using GamaEdtech.Domain.Specification.Identity;
    using GamaEdtech.Domain.Specification.Post;
    using GamaEdtech.Presentation.ViewModel.ApplicationSettings;
    using GamaEdtech.Presentation.ViewModel.Post;

    using Microsoft.AspNetCore.Mvc;

    [Common.DataAnnotation.Area(nameof(Admin), "Admin")]
    [Route("api/v{version:apiVersion}/[area]/[controller]")]
    [ApiVersion("1.0")]
    [Permission(Roles = [nameof(Role.Admin)])]
    public class PostsController(Lazy<ILogger<PostsController>> logger, Lazy<IPostService> postService, Lazy<IIdentityService> identityService
        , Lazy<IGlobalService> globalService)
        : ApiControllerBase<PostsController>(logger)
    {
        private static ISpecification<T>? AllOf<T>(List<ISpecification<T>> specifications) => specifications.Count == 0 ? null : specifications.Aggregate((left, right) => left.And(right));

        [HttpGet(""), Produces<ApiResponse<ListDataSource<ManagedPostsResponseViewModel>>>()]
        public async Task<IActionResult<ListDataSource<ManagedPostsResponseViewModel>>> GetPosts([NotNull, FromQuery] AdminPostsRequestViewModel request)
        {
            try
            {
                List<ISpecification<Post>> specifications = [];
                if (request.Status is not null)
                {
                    specifications.Add(new StatusEqualsSpecification<Post>(request.Status));
                }

                if (!string.IsNullOrEmpty(request.Title))
                {
                    specifications.Add(new TitleContainsSpecification(request.Title));
                }

                if (!string.IsNullOrEmpty(request.Username))
                {
                    var userIds = await identityService.Value.GetUserIdsAsync(new NameContainsSpecification(request.Username));
                    if (userIds.Data?.Count > 0)
                    {
                        specifications.Add(new CreationUserIdContainsSpecification<Post, ApplicationUser, long>(userIds.Data));
                    }
                }

                if (!string.IsNullOrEmpty(request.Email))
                {
                    var userIds = await identityService.Value.GetUserIdsAsync(new EmailEqualsSpecification(request.Email));
                    if (userIds.Data?.Count > 0)
                    {
                        specifications.Add(new CreationUserIdContainsSpecification<Post, ApplicationUser, long>(userIds.Data));
                    }
                }

                if (request.StartDate.HasValue || request.EndDate.HasValue)
                {
                    specifications.Add(new CreationDateBetweenSpecification<Post>(request.StartDate, request.EndDate));
                }

                var result = await postService.Value.GetPostsAsync(new ListRequestDto<Post>
                {
                    PagingDto = request.PagingDto,
                    Specification = AllOf(specifications),
                });
                return Ok<ListDataSource<ManagedPostsResponseViewModel>>(new(result.Errors)
                {
                    Data = result.Data.List is null ? new() : new()
                    {
                        List = result.Data.List.Select(t => new ManagedPostsResponseViewModel
                        {
                            Id = t.Id,
                            Title = t.Title,
                            Slug = t.Slug,
                            ImageUri = t.ImageUri,
                            Status = t.Status,
                            RejectionComment = t.RejectionComment,
                            CreationUser = t.CreationUser,
                            CreationDate = t.CreationDate,
                            PublishDate = t.PublishDate,
                            VisibilityType = t.VisibilityType,
                        }),
                        TotalRecordsCount = result.Data.TotalRecordsCount,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ListDataSource<ManagedPostsResponseViewModel>>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpGet("{postId:long}"), Produces<ApiResponse<PostEditResponseViewModel>>()]
        public async Task<IActionResult<PostEditResponseViewModel>> GetPost([FromRoute] long postId)
        {
            try
            {
                var result = await postService.Value.GetPostForEditAsync(new IdEqualsSpecification<Post, long>(postId));

                return Ok<PostEditResponseViewModel>(new(result.Errors)
                {
                    Data = result.Data is null ? null : new()
                    {
                        Id = result.Data.Id,
                        Title = result.Data.Title,
                        Slug = result.Data.Slug,
                        Summary = result.Data.Summary,
                        Body = result.Data.Body,
                        ImageUri = result.Data.ImageUri,
                        PodcastUri = result.Data.PodcastUri,
                        Keywords = result.Data.Keywords,
                        VisibilityType = result.Data.VisibilityType,
                        PublishDate = result.Data.PublishDate,
                        Status = result.Data.Status,
                        RejectionComment = result.Data.RejectionComment,
                        Tags = result.Data.Tags,
                        LocalizedValues = result.Data.LocalizedValues?.Select(t => new PostLocalizedValueViewModel
                        {
                            LanguageId = t.LanguageId,
                            Title = t.Title,
                            Summary = t.Summary,
                            Body = t.Body,
                        }),
                    },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<PostEditResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpPatch("{postId:long}/confirm"), Produces<ApiResponse<bool>>()]
        public async Task<IActionResult> ConfirmPost([FromRoute] long postId)
        {
            try
            {
                var result = await postService.Value.ConfirmPostAsync(postId, true);

                return Ok(new ApiResponse<bool>(result.Errors) { Data = result.Data, });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok(new ApiResponse<bool> { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpPatch("{postId:long}/reject"), Produces<ApiResponse<bool>>()]
        public async Task<IActionResult> RejectPost([FromRoute] long postId, [NotNull, FromBody] RejectRequestViewModel request)
        {
            try
            {
                var result = await postService.Value.RejectPostAsync(postId, request.Comment, User.UserId());

                return Ok(new ApiResponse<bool>(result.Errors) { Data = result.Data, });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok(new ApiResponse<bool> { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpPut("{postId:long}"), Produces<ApiResponse<ManagePostResponseViewModel>>()]
        public async Task<IActionResult<ManagePostResponseViewModel>> UpdatePost([FromRoute] long postId, [NotNull, FromForm] UpdatePostRequestViewModel request)
        {
            try
            {
                ManagePostRequestDto dto = new()
                {
                    Id = postId,
                    UserId = User.UserId(),
                    IsAdmin = true,
                    Body = request.Body,
                    Image = request.Image,
                    Podcast = request.Podcast,
                    RemovePodcast = request.RemovePodcast,
                    Keywords = request.Keywords,
                    PublishDate = request.PublishDate,
                    Slug = request.Slug,
                    Summary = request.Summary,
                    Tags = request.Tags,
                    Title = request.Title,
                    VisibilityType = request.VisibilityType,
                    LocalizedValues = request.LocalizedValues?.Select(t => new PostLocalizedValueDto
                    {
                        LanguageId = t.LanguageId.GetValueOrDefault(),
                        Title = t.Title,
                        Summary = t.Summary,
                        Body = t.Body,
                    }),
                };
                var result = await postService.Value.ManagePostAsync(dto);

                return Ok<ManagePostResponseViewModel>(new(result.Errors)
                {
                    Data = new() { Id = result.Data, },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ManagePostResponseViewModel>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpDelete("{postId:long}"), Produces<ApiResponse<bool>>()]
        public async Task<IActionResult> RemovePost([FromRoute] long postId)
        {
            try
            {
                var specification = new IdEqualsSpecification<Post, long>(postId);
                var result = await postService.Value.RemovePostAsync(specification);
                return Ok(new ApiResponse<bool>
                {
                    Errors = result.Errors,
                    Data = result.Data
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok(new ApiResponse<bool> { Errors = [new() { Message = exc.Message }] });
            }
        }

        #region Comments

        [HttpGet("comments"), Produces<ApiResponse<ListDataSource<ManagedPostCommentsResponseViewModel>>>()]
        public async Task<IActionResult<ListDataSource<ManagedPostCommentsResponseViewModel>>> GetPostComments([NotNull, FromQuery] AdminPostCommentsRequestViewModel request)
        {
            try
            {
                List<ISpecification<PostComment>> specifications = [];
                if (request.Status is not null)
                {
                    specifications.Add(new StatusEqualsSpecification<PostComment>(request.Status));
                }

                if (!string.IsNullOrEmpty(request.CommenterName))
                {
                    var userIds = await identityService.Value.GetUserIdsAsync(new NameContainsSpecification(request.CommenterName));
                    if (userIds.Data?.Count > 0)
                    {
                        specifications.Add(new CreationUserIdContainsSpecification<PostComment, ApplicationUser, long>(userIds.Data));
                    }
                }

                if (!string.IsNullOrEmpty(request.CommenterEmail))
                {
                    var userIds = await identityService.Value.GetUserIdsAsync(new EmailEqualsSpecification(request.CommenterEmail));
                    if (userIds.Data?.Count > 0)
                    {
                        specifications.Add(new CreationUserIdContainsSpecification<PostComment, ApplicationUser, long>(userIds.Data));
                    }
                }

                if (request.StartDate.HasValue || request.EndDate.HasValue)
                {
                    specifications.Add(new CreationDateBetweenSpecification<PostComment>(request.StartDate, request.EndDate));
                }

                var result = await postService.Value.GetPostCommentsAsync(new ListRequestDto<PostComment>
                {
                    PagingDto = request.PagingDto,
                    Specification = AllOf(specifications),
                });
                return Ok(new ApiResponse<ListDataSource<ManagedPostCommentsResponseViewModel>>(result.Errors)
                {
                    Data = result.Data.List is null ? new() : new()
                    {
                        List = result.Data.List.Select(t => new ManagedPostCommentsResponseViewModel
                        {
                            Id = t.Id,
                            PostId = t.PostId,
                            PostTitle = t.PostTitle,
                            Comment = t.Comment,
                            CreationUser = t.CreationUser,
                            CreationDate = t.CreationDate,
                            Status = t.Status,
                            RejectionComment = t.RejectionComment,
                        }),
                        TotalRecordsCount = result.Data.TotalRecordsCount,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok(new ApiResponse<ListDataSource<ManagedPostCommentsResponseViewModel>>(new Error { Message = exc.Message }));
            }
        }

        [HttpPatch("comments/{commentId:long}/confirm"), Produces<ApiResponse<bool>>()]
        public async Task<IActionResult> ConfirmPostComment([FromRoute] long commentId)
        {
            try
            {
                var result = await postService.Value.ConfirmPostCommentAsync(commentId, true);

                return Ok(new ApiResponse<bool>(result.Errors) { Data = result.Data, });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok(new ApiResponse<bool> { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpPatch("comments/{commentId:long}/reject"), Produces<ApiResponse<bool>>()]
        public async Task<IActionResult> RejectPostComment([FromRoute] long commentId, [NotNull, FromBody] RejectRequestViewModel request)
        {
            try
            {
                var result = await postService.Value.RejectPostCommentAsync(commentId, request.Comment, User.UserId());

                return Ok(new ApiResponse<bool>(result.Errors) { Data = result.Data, });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok(new ApiResponse<bool> { Errors = [new() { Message = exc.Message }] });
            }
        }

        #endregion

        #region Site Map

        [HttpGet("site-maps"), Produces<ApiResponse<ListDataSource<SiteMapListResponseViewModel>>>()]
        [Display(Name = "Posts Site Maps List")]
        public async Task<IActionResult<ListDataSource<SiteMapListResponseViewModel>>> GetSiteMapsList([NotNull, FromQuery] SiteMapListRequestViewModel request)
        {
            try
            {
                var result = await globalService.Value.GetSiteMapsAsync(new ListRequestDto<SiteMap>
                {
                    PagingDto = request.PagingDto,
                    Specification = new ItemTypeEqualsSpecification(ItemType.Post),
                });
                if (result.OperationResult is not Constants.OperationResult.Succeeded)
                {
                    return Ok(new ApiResponse<ListDataSource<SiteMapListResponseViewModel>>(result.Errors));
                }

                if (result.Data.List is null)
                {
                    return Ok(new ApiResponse<ListDataSource<SiteMapListResponseViewModel>>());
                }

                var names = await postService.Value.GetPostsTitleAsync(new IdContainsSpecification<Post, long>(result.Data.List.Select(t => t.IdentifierId)));
                if (names.OperationResult is not Constants.OperationResult.Succeeded)
                {
                    return Ok(new ApiResponse<ListDataSource<SiteMapListResponseViewModel>>(names.Errors));
                }

                var lst = result.Data.List.Select(t => new SiteMapListResponseViewModel
                {
                    Id = t.Id,
                    IdentifierId = t.IdentifierId,
                    ChangeFrequency = t.ChangeFrequency,
                    Priority = t.Priority,
                    Title = names.Data?.Find(s => s.Key == t.IdentifierId).Value,
                });
                return Ok(new ApiResponse<ListDataSource<SiteMapListResponseViewModel>>(result.Errors)
                {
                    Data = new()
                    {
                        List = lst,
                        TotalRecordsCount = result.Data.TotalRecordsCount,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok(new ApiResponse<ListDataSource<SiteMapListResponseViewModel>>(new Error { Message = exc.Message }));
            }
        }

        [HttpPost("{postId:long}/site-maps"), Produces<ApiResponse<ManageSiteMapResponseViewModel>>()]
        [Display(Name = "Create Post Site Maps")]
        public async Task<IActionResult<ManageSiteMapResponseViewModel>> CreateSiteMap([FromRoute] long postId, [NotNull] ManageSiteMapRequestViewModel request)
        {
            try
            {
                var result = await globalService.Value.ManageSiteMapAsync(new()
                {
                    IdentifierId = postId,
                    ItemType = ItemType.Post,
                    ChangeFrequency = request.ChangeFrequency,
                    Priority = request.Priority,
                });
                return Ok<ManageSiteMapResponseViewModel>(new(result.Errors)
                {
                    Data = new() { Id = result.Data, },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ManageSiteMapResponseViewModel>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpPut("{postId:long}/site-maps/{id:long}"), Produces(typeof(ApiResponse<ManageSiteMapResponseViewModel>))]
        [Display(Name = "Edit Post Site Maps")]
        public async Task<IActionResult<ManageSiteMapResponseViewModel>> UpdateSiteMap([FromRoute] long postId, [FromRoute] long id, [NotNull] ManageSiteMapRequestViewModel request)
        {
            try
            {
                var result = await globalService.Value.ManageSiteMapAsync(new()
                {
                    Id = id,
                    IdentifierId = postId,
                    ItemType = ItemType.Post,
                    ChangeFrequency = request.ChangeFrequency,
                    Priority = request.Priority,
                });
                return Ok<ManageSiteMapResponseViewModel>(new(result.Errors)
                {
                    Data = new() { Id = result.Data }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ManageSiteMapResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpDelete("{postId:long}/site-maps/{id:long}"), Produces<ApiResponse<bool>>()]
        [Display(Name = "Remove Post Site Map")]
        public async Task<IActionResult> RemoveSiteMap([FromRoute] long postId, [FromRoute] long id)
        {
            try
            {
                var specification = new IdEqualsSpecification<SiteMap, long>(id)
                    .And(new ItemTypeEqualsSpecification(ItemType.Post))
                    .And(new IdentifierIdEqualsSpecification<SiteMap>(postId));
                var result = await globalService.Value.RemoveSiteMapAsync(specification);
                return Ok(new ApiResponse<bool>
                {
                    Errors = result.Errors,
                    Data = result.Data
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok(new ApiResponse<bool> { Errors = [new() { Message = exc.Message }] });
            }
        }

        #endregion
    }
}
