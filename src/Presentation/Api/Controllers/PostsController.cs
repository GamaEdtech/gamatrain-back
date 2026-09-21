namespace GamaEdtech.Presentation.Api.Controllers
{
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
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
    using GamaEdtech.Domain.Specification.Post;
    using GamaEdtech.Presentation.ViewModel.Post;
    using GamaEdtech.Presentation.ViewModel.Tag;

    using Hangfire;

    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;

    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    [Permission(policy: null)]
    public class PostsController(Lazy<ILogger<PostsController>> logger, Lazy<IPostService> postService
        , Lazy<IGlobalService> globalService)
        : ApiControllerBase<PostsController>(logger)
    {
        [HttpGet(""), Produces<ApiResponse<ListDataSource<PostsResponseViewModel>>>()]
        [ResponseCache(Location = ResponseCacheLocation.Any, Duration = 120)]
        [AllowAnonymous]
        public async Task<IActionResult<ListDataSource<PostsResponseViewModel>>> GetPosts([NotNull, FromQuery] PostsRequestViewModel request)
        {
            try
            {
                ISpecification<Post>? specification = new PublishedPostSpecification();

                if (request.TagId.HasValue)
                {
                    specification = specification.And(new TagIncludedSpecification(request.TagId.Value));
                }

                if (request.VisibilityType is not null)
                {
                    specification = specification.And(new VisibilityTypeEqualsSpecification(request.VisibilityType));
                }

                if (request.PublishDate.HasValue)
                {
                    specification = specification.And(new PublishDateEqualsSpecification(request.PublishDate.Value));
                }

                if (!string.IsNullOrEmpty(request.Title))
                {
                    specification = specification.And(new TitleContainsSpecification(request.Title));
                }

                var result = await postService.Value.GetPostsAsync(new ListRequestDto<Post>
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
                            Slug = t.Slug,
                            Summary = t.Summary,
                            LikeCount = t.LikeCount,
                            DislikeCount = t.DislikeCount,
                            ImageUri = t.ImageUri,
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

                return Ok<ListDataSource<PostsResponseViewModel>>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpGet("random"), Produces<ApiResponse<ListDataSource<PostsResponseViewModel>>>()]
        [ResponseCache(Location = ResponseCacheLocation.Any, Duration = 60)]
        [AllowAnonymous]
        public async Task<IActionResult<ListDataSource<PostsResponseViewModel>>> GetRandomPosts([NotNull, FromQuery] RandomPostsRequestViewModel request) => await GetPosts(new()
        {
            PagingDto = new()
            {
                PageFilter = new() { Skip = 0, Size = request.Size },
                SortFilter = [new() { SortType = Constants.SortType.Random }],
            },
        });

        [HttpGet("{postId:long}"), Produces<ApiResponse<PostResponseViewModel>>()]
        [ResponseCache(Location = ResponseCacheLocation.Any, Duration = 300)]
        [AllowAnonymous]
        public async Task<IActionResult<PostResponseViewModel>> GetPost([FromRoute] long postId)
        {
            try
            {
                var specification = new IdEqualsSpecification<Post, long>(postId).And(new PublishedPostSpecification());
                var result = await postService.Value.GetPostAsync(specification);
                if (result.OperationResult is not Constants.OperationResult.Succeeded)
                {
                    return Ok<PostResponseViewModel>(new(result.Errors));
                }

                _ = BackgroundJob.Enqueue(() => postService.Value.IncreasePostViewAsync(postId));

                return Ok<PostResponseViewModel>(new()
                {
                    Data = result.Data is null ? null : new()
                    {
                        Title = result.Data.Title,
                        Slug = result.Data.Slug,
                        Summary = result.Data.Summary,
                        Body = result.Data.Body,
                        ImageUri = result.Data.ImageUri,
                        PodcastUri = result.Data.PodcastUri,
                        LikeCount = result.Data.LikeCount,
                        LikedByCurrentUser = result.Data.LikedByCurrentUser,
                        DislikeCount = result.Data.DislikeCount,
                        DislikedByCurrentUser = result.Data.DislikedByCurrentUser,
                        CreationUser = result.Data.CreationUser,
                        CreationUserAvatarUri = result.Data.CreationUserAvatarUri,
                        VisibilityType = result.Data.VisibilityType,
                        PublishDate = result.Data.PublishDate,
                        Keywords = result.Data.Keywords,
                        ViewCount = result.Data.ViewCount + 1,  //plus 1 for current view
                        Tags = result.Data.Tags?.Select(t => new TagResponseViewModel
                        {
                            Id = t.Id,
                            Icon = t.Icon,
                            Name = t.Name,
                            TagType = t.TagType,
                        }),
                        NextId = result.Data.NextId,
                        PreviousId = result.Data.PreviousId,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<PostResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpDelete("{postId:long}"), Produces<ApiResponse<bool>>()]
        public async Task<IActionResult<bool>> RemovePost([FromRoute] long postId)
        {
            try
            {
                var isCreator = await postService.Value.IsCreatorOfPostAsync(postId, User.UserId());
                if (!isCreator.Data)
                {
                    return Ok(new ApiResponse<bool> { Errors = [new() { Message = "Invalid Request" }] });
                }

                var result = await postService.Value.RemovePostAsync(new IdEqualsSpecification<Post, long>(postId));
                return Ok<bool>(new(result.Errors) { Data = result.Data });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<bool>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpPatch("{postId:long}/like"), Produces<ApiResponse<bool>>()]
        public async Task<IActionResult<bool>> LikePost([FromRoute] long postId)
        {
            try
            {
                var result = await postService.Value.LikePostAsync(new()
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

        [HttpPatch("{postId:long}/dislike"), Produces<ApiResponse<bool>>()]
        public async Task<IActionResult<bool>> DislikePost([FromRoute] long postId)
        {
            try
            {
                var result = await postService.Value.DislikePostAsync(new()
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

        [HttpGet("slugs/generate"), Produces<ApiResponse<string>>()]
        public async Task<IActionResult<string>> GenerateSlug([FromQuery, Required] string title)
        {
            try
            {
                var slug = Globals.Slugify(title);
                var result = await GenerateSlugAsync(slug);

                return Ok<string>(new()
                {
                    Data = result,
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<string>(new(new Error { Message = exc.Message }));
            }

            async Task<string?> GenerateSlugAsync(string? slug)
            {
                var result = await postService.Value.PostExistsAsync(new SlugEqualsSpecification(slug));
                if (result.OperationResult is Constants.OperationResult.Succeeded && !result.Data)
                {
                    return slug;
                }

                slug += 1;
                return await GenerateSlugAsync(slug);
            }
        }

        [HttpGet("slugs/validate"), Produces<ApiResponse<bool>>()]
        public async Task<IActionResult<bool>> ValidateSlug([FromQuery, Required] string? slug)
        {
            try
            {
                var result = await postService.Value.PostExistsAsync(new SlugEqualsSpecification(slug));
                return Ok<bool>(new(result.Errors)
                {
                    Data = !result.Data,
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<bool>(new(new Error { Message = exc.Message }));
            }
        }

        #region My Posts

        [HttpGet("mine"), Produces<ApiResponse<ListDataSource<ManagedPostsResponseViewModel>>>()]
        public async Task<IActionResult<ListDataSource<ManagedPostsResponseViewModel>>> GetMyPosts([NotNull, FromQuery] MyPostsRequestViewModel request)
        {
            try
            {
                ISpecification<Post> specification = new CreationUserIdEqualsSpecification<Post, ApplicationUser, long>(User.UserId());
                if (request.Status is not null)
                {
                    specification = specification.And(new StatusEqualsSpecification<Post>(request.Status));
                }

                if (request.StartDate.HasValue || request.EndDate.HasValue)
                {
                    specification = specification.And(new CreationDateBetweenSpecification<Post>(request.StartDate, request.EndDate));
                }

                var result = await postService.Value.GetPostsAsync(new ListRequestDto<Post>
                {
                    PagingDto = request.PagingDto,
                    Specification = specification,
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

        [HttpGet("mine/{postId:long}"), Produces<ApiResponse<PostEditResponseViewModel>>()]
        public async Task<IActionResult<PostEditResponseViewModel>> GetMyPost([FromRoute] long postId)
        {
            try
            {
                var specification = new IdEqualsSpecification<Post, long>(postId)
                    .And(new CreationUserIdEqualsSpecification<Post, ApplicationUser, long>(User.UserId()));
                var result = await postService.Value.GetPostForEditAsync(specification);

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

        [HttpPost(""), Produces<ApiResponse<ManagePostResponseViewModel>>()]
        public async Task<IActionResult<ManagePostResponseViewModel>> CreatePost([NotNull, FromForm] CreatePostRequestViewModel request)
        {
            try
            {
                var result = await postService.Value.ManagePostAsync(new()
                {
                    UserId = User.UserId(),
                    Title = request.Title,
                    Slug = request.Slug,
                    Summary = request.Summary,
                    Body = request.Body,
                    Image = request.Image,
                    Podcast = request.Podcast,
                    Tags = request.Tags,
                    PublishDate = request.PublishDate,
                    VisibilityType = request.VisibilityType,
                    Keywords = request.Keywords,
                    Draft = request.Draft,
                    LocalizedValues = request.LocalizedValues?.Select(t => new PostLocalizedValueDto
                    {
                        LanguageId = t.LanguageId.GetValueOrDefault(),
                        Title = t.Title,
                        Summary = t.Summary,
                        Body = t.Body,
                    }),
                });

                return Ok<ManagePostResponseViewModel>(new(result.Errors)
                {
                    Data = new() { Id = result.Data, },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ManagePostResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpPut("{postId:long}"), Produces<ApiResponse<ManagePostResponseViewModel>>()]
        public async Task<IActionResult<ManagePostResponseViewModel>> UpdatePost([FromRoute] long postId, [NotNull, FromForm] UpdatePostRequestViewModel request)
        {
            try
            {
                var isCreator = await postService.Value.IsCreatorOfPostAsync(postId, User.UserId());
                if (!isCreator.Data)
                {
                    return Ok<ManagePostResponseViewModel>(new(new Error { Message = "InvalidRequest" }));
                }

                var result = await postService.Value.ManagePostAsync(new()
                {
                    Id = postId,
                    UserId = User.UserId(),
                    Title = request.Title,
                    Slug = request.Slug,
                    Summary = request.Summary,
                    Body = request.Body,
                    Image = request.Image,
                    Podcast = request.Podcast,
                    RemovePodcast = request.RemovePodcast,
                    Tags = request.Tags,
                    PublishDate = request.PublishDate,
                    VisibilityType = request.VisibilityType,
                    Keywords = request.Keywords,
                    Draft = request.Draft,
                    LocalizedValues = request.LocalizedValues?.Select(t => new PostLocalizedValueDto
                    {
                        LanguageId = t.LanguageId.GetValueOrDefault(),
                        Title = t.Title,
                        Summary = t.Summary,
                        Body = t.Body,
                    }),
                });

                return Ok<ManagePostResponseViewModel>(new(result.Errors)
                {
                    Data = new() { Id = result.Data, },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ManagePostResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        #endregion

        #region Comments

        [HttpGet("{postId:long}/comments"), Produces<ApiResponse<ListDataSource<PostCommentsResponseViewModel>>>()]
        [AllowAnonymous]
        public async Task<IActionResult<ListDataSource<PostCommentsResponseViewModel>>> GetPostComments([FromRoute] long postId, [NotNull, FromQuery] PostCommentsRequestViewModel request)
        {
            try
            {
                var result = await postService.Value.GetPostCommentsAsync(new ListRequestDto<PostComment>
                {
                    PagingDto = request.PagingDto,
                    Specification = new PostIdEqualsSpecification<PostComment>(postId).And(new StatusEqualsSpecification<PostComment>(Status.Confirmed)),
                });
                return Ok<ListDataSource<PostCommentsResponseViewModel>>(new(result.Errors)
                {
                    Data = result.Data.List is null ? new() : new()
                    {
                        List = result.Data.List.Select(t => new PostCommentsResponseViewModel
                        {
                            Id = t.Id,
                            Comment = t.Comment,
                            CreationDate = t.CreationDate,
                            CreationUser = t.CreationUser,
                            CreationUserAvatarUri = t.CreationUserAvatarUri,
                            DislikeCount = t.DislikeCount,
                            LikeCount = t.LikeCount,
                            LikedByCurrentUser = t.LikedByCurrentUser,
                            DislikedByCurrentUser = t.DislikedByCurrentUser,
                        }),
                        TotalRecordsCount = result.Data.TotalRecordsCount,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ListDataSource<PostCommentsResponseViewModel>>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpPost("{postId:long}/comments"), Produces<ApiResponse<ManagePostCommentResponseViewModel>>()]
        public async Task<IActionResult<ManagePostCommentResponseViewModel>> CreatePostComment([FromRoute] long postId, [NotNull] ManagePostCommentRequestViewModel request)
        {
            try
            {
                var validateCaptcha = await globalService.Value.VerifyCaptchaAsync(request.Captcha);
                if (!validateCaptcha.Data)
                {
                    return Ok<ManagePostCommentResponseViewModel>(new(new Error { Message = "Invalid Captcha" }));
                }

                var result = await postService.Value.CreatePostCommentAsync(new()
                {
                    UserId = User.UserId(),
                    PostId = postId,
                    Comment = request.Comment,
                });
                return Ok<ManagePostCommentResponseViewModel>(new(result.Errors)
                {
                    Data = new() { Id = result.Data, },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ManagePostCommentResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpPatch("{postId:long}/comments/{commentId:long}/like"), Produces<ApiResponse<bool>>()]
        public async Task<IActionResult<bool>> LikePostComment([FromRoute] long postId, [FromRoute] long commentId)
        {
            try
            {
                var result = await postService.Value.LikePostCommentAsync(new()
                {
                    CommentId = commentId,
                    PostId = postId,
                    UserId = User.UserId(),
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

        [HttpPatch("{postId:long}/comments/{commentId:long}/dislike"), Produces<ApiResponse<bool>>()]
        public async Task<IActionResult<bool>> DislikePostComment([FromRoute] long postId, [FromRoute] long commentId)
        {
            try
            {
                var result = await postService.Value.DislikePostCommentAsync(new()
                {
                    CommentId = commentId,
                    PostId = postId,
                    UserId = User.UserId(),
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

        #endregion
    }
}
