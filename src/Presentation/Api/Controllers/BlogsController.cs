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
    using GamaEdtech.Data.Dto.Blog;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Domain.Specification;
    using GamaEdtech.Domain.Specification.Post;
    using GamaEdtech.Presentation.ViewModel.Blog;
    using GamaEdtech.Presentation.ViewModel.Tag;

    using Hangfire;

    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;

    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    [Permission(policy: null)]
    public class BlogsController(Lazy<ILogger<BlogsController>> logger, Lazy<IBlogService> blogService
        , Lazy<IContributionService> contributionService, Lazy<IGlobalService> globalService, Lazy<IFileService> fileService)
        : ApiControllerBase<BlogsController>(logger)
    {
        [HttpGet("posts"), Produces<ApiResponse<ListDataSource<PostsResponseViewModel>>>()]
        [ResponseCache(Location = ResponseCacheLocation.Any, Duration = 120)]
        [AllowAnonymous]
        public async Task<IActionResult<ListDataSource<PostsResponseViewModel>>> GetPosts([NotNull, FromQuery] PostsRequestViewModel request)
        {
            try
            {
                ISpecification<Post>? specification = new PublishDateSpecification();

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

        [HttpGet("posts/random"), Produces<ApiResponse<ListDataSource<PostsResponseViewModel>>>()]
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

        [HttpGet("posts/{postId:long}"), Produces<ApiResponse<PostResponseViewModel>>()]
        [ResponseCache(Location = ResponseCacheLocation.Any, Duration = 300)]
        [AllowAnonymous]
        public async Task<IActionResult<PostResponseViewModel>> GetPost([FromRoute] long postId)
        {
            try
            {
                var specification = new IdEqualsSpecification<Post, long>(postId).And(new PublishDateSpecification());
                var result = await blogService.Value.GetPostAsync(specification);
                if (result.OperationResult is not Constants.OperationResult.Succeeded)
                {
                    return Ok<PostResponseViewModel>(new(result.Errors));
                }

                _ = BackgroundJob.Enqueue(() => blogService.Value.IncreasePostViewAsync(postId));

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

        [HttpDelete("posts/{postId:long}"), Produces<ApiResponse<bool>>()]
        public async Task<IActionResult<bool>> RemovePost([FromRoute] long postId)
        {
            try
            {
                var isCreator = await blogService.Value.IsCreatorOfPostAsync(postId, User.UserId());
                if (!isCreator.Data)
                {
                    return Ok(new ApiResponse<bool> { Errors = [new() { Message = "Invalid Request" }] });
                }

                var result = await blogService.Value.RemovePostAsync(new IdEqualsSpecification<Post, long>(postId));
                return Ok<bool>(new(result.Errors) { Data = result.Data });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<bool>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpPatch("posts/{postId:long}/like"), Produces<ApiResponse<bool>>()]
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
                var result = await blogService.Value.PostExistsAsync(new SlugEqualsSpecification(slug));
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
                var result = await blogService.Value.PostExistsAsync(new SlugEqualsSpecification(slug));
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

        #region Contributions

        [HttpGet("contributions"), Produces<ApiResponse<ListDataSource<PostContributionListResponseViewModel>>>()]
        public async Task<IActionResult<ListDataSource<PostContributionListResponseViewModel>>> GetPostContributionList([NotNull, FromQuery] PostContributionListRequestViewModel request)
        {
            try
            {
                var specification = new CreationUserIdEqualsSpecification<Contribution, ApplicationUser, long>(User.UserId())
                        .And(new CategoryTypeEqualsSpecification<Contribution>(CategoryType.Post));
                // No Status filter -> every status except Deleted (mirrors ManagePostContributionAsync's own
                // .AndNot(new StatusEqualsSpecification<Contribution>(Status.Deleted)) guard) - a caller who
                // wants deleted contributions back must ask for Status=Deleted explicitly.
                specification = request.Status is not null
                    ? specification.And(new StatusEqualsSpecification<Contribution>(request.Status))
                    : specification.AndNot(new StatusEqualsSpecification<Contribution>(Status.Deleted));

                if (request.StartDate.HasValue || request.EndDate.HasValue)
                {
                    specification = specification.And(new CreationDateBetweenSpecification<Contribution>(request.StartDate, request.EndDate));
                }

                var result = await contributionService.Value.GetContributionsAsync<PostContributionDto>(new ListRequestDto<Contribution>
                {
                    PagingDto = request.PagingDto,
                    Specification = specification,
                }, true);
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
                            Title = t.Data?.Title,
                            PostId = t.IdentifierId,
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
        public async Task<IActionResult<PostContributionResponseViewModel>> GetPostContribution([FromRoute] long contributionId)
        {
            try
            {
                var specification = new IdEqualsSpecification<Contribution, long>(contributionId)
                    .And(new CreationUserIdEqualsSpecification<Contribution, ApplicationUser, long>(User.UserId()))
                    .And(new CategoryTypeEqualsSpecification<Contribution>(CategoryType.Post));
                var result = await contributionService.Value.GetContributionAsync<PostContributionDto>(specification);

                PostContributionResponseViewModel? viewModel = null;
                if (result.Data?.Data is not null)
                {
                    viewModel = result.Data.Data is null ? null : MapFrom(result.Data.Data);
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
                    ImageUri = fileService.Value.GetStaticFileUrl(new() { FileId = dto.ImageId, ContainerType = ContainerType.Post, }),
                    PodcastUri = fileService.Value.GetStaticFileUrl(new() { FileId = dto.PodcastId, ContainerType = ContainerType.Post, }),
                    PublishDate = dto.PublishDate.GetValueOrDefault(),
                    VisibilityType = dto.VisibilityType!,
                    Keywords = dto.Keywords,
                    Slug = dto.Slug,
                    PostId = result.Data.IdentifierId,
                    Draft = dto.Draft,
                    LocalizedValues = dto.LocalizedValues?.Select(t => new PostLocalizedValueViewModel
                    {
                        LanguageId = t.LanguageId,
                        Title = t.Title,
                        Summary = t.Summary,
                        Body = t.Body,
                    }),
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<PostContributionResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpPost("contributions"), Produces<ApiResponse<ManagePostContributionResponseViewModel>>()]
        public async Task<IActionResult<ManagePostContributionResponseViewModel>> CreatePostContribution([NotNull, FromForm] PostContributionViewModel request)
        {
            try
            {
                ManagePostContributionRequestDto dto = new()
                {
                    UserId = User.UserId(),
                    Title = request.Title,
                    Slug = request.Slug,
                    Summary = request.Summary,
                    Body = request.Body,
                    Image = request.Image,
                    Podcast = request.Podcast,
                    Tags = request.Tags,
                    PublishDate = request.PublishDate.GetValueOrDefault(),
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
                };
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
        public async Task<IActionResult<ManagePostContributionResponseViewModel>> UpdatePostContribution([FromRoute] long contributionId, [NotNull, FromForm] UpdatePostContributionViewModel request)
        {
            try
            {
                var isCreator = await contributionService.Value.IsCreatorOfContributionAsync(contributionId, User.UserId());
                if (!isCreator.Data)
                {
                    return Ok<ManagePostContributionResponseViewModel>(new(new Error { Message = "InvalidRequest" }));
                }

                ManagePostContributionRequestDto dto = new()
                {
                    ContributionId = contributionId,
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
                };
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

        #region Comments

        [HttpGet("posts/{postId:long}/comments"), Produces<ApiResponse<ListDataSource<PostCommentsResponseViewModel>>>()]
        [AllowAnonymous]
        public async Task<IActionResult<ListDataSource<PostCommentsResponseViewModel>>> GetPostComments([FromRoute] long postId, [NotNull, FromQuery] PostCommentsRequestViewModel request)
        {
            try
            {
                var result = await blogService.Value.GetPostCommentsAsync(new ListRequestDto<PostComment>
                {
                    PagingDto = request.PagingDto,
                    Specification = new PostIdEqualsSpecification<PostComment>(postId),
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

        [HttpPost("posts/{postId:long}/comments"), Produces<ApiResponse<ManagePostCommentResponseViewModel>>()]
        public async Task<IActionResult<ManagePostCommentResponseViewModel>> CreatePostComment([FromRoute] long postId, [NotNull] ManagePostCommentRequestViewModel request)
        {
            try
            {
                var validateCaptcha = await globalService.Value.VerifyCaptchaAsync(request.Captcha);
                if (!validateCaptcha.Data)
                {
                    return Ok<ManagePostCommentResponseViewModel>(new(new Error { Message = "Invalid Captcha" }));
                }

                var result = await blogService.Value.CreatePostCommentContributionAsync(new()
                {
                    UserId = User.UserId(),
                    PostId = postId,
                    CommentContribution = new()
                    {
                        Comment = request.Comment,
                        PostId = postId,
                        CreationDate = DateTimeOffset.UtcNow,
                        CreationUserId = User.UserId(),
                    }
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

        [HttpPatch("posts/{postId:long}/comments/{commentId:long}/like"), Produces<ApiResponse<bool>>()]
        public async Task<IActionResult<bool>> LikePostComment([FromRoute] long postId, [FromRoute] long commentId)
        {
            try
            {
                var result = await blogService.Value.LikePostCommentAsync(new()
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

        [HttpPatch("posts/{postId:long}/comments/{commentId:long}/dislike"), Produces<ApiResponse<bool>>()]
        public async Task<IActionResult<bool>> DislikePostComment([FromRoute] long postId, [FromRoute] long commentId)
        {
            try
            {
                var result = await blogService.Value.DislikePostCommentAsync(new()
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
