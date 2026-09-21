namespace GamaEdtech.Application.Service
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;
    using System.Threading.Tasks;

    using EntityFramework.Exceptions.Common;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Core.Extensions.Linq;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Common.DataAccess.Specification.Impl;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Security;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.ApplicationSettings;
    using GamaEdtech.Data.Dto.Post;
    using GamaEdtech.Data.Dto.SiteMap;
    using GamaEdtech.Data.Dto.Tag;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Domain.Specification;
    using GamaEdtech.Domain.Specification.Post;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    public class PostService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor, Lazy<IStringLocalizer<PostService>> localizer
        , Lazy<ILogger<PostService>> logger, Lazy<IReactionService> reactionService, Lazy<IFileService> fileService, Lazy<IIdentityService> identityService, Lazy<IEmailService> emailService
        , Lazy<ITransactionService> transactionService, Lazy<ITagService> tagService, Lazy<IConfiguration> configuration, Lazy<IApplicationSettingsService> applicationSettingsService, Lazy<IContentLocalizationService> contentLocalizationService)
        : LocalizableServiceBase<PostService>(unitOfWorkProvider, httpContextAccessor, localizer, logger), IPostService, ISiteMapHandler
    {
        public async Task<ResultData<ListDataSource<PostsDto>>> GetPostsAsync(ListRequestDto<Post>? requestDto = null)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var lst = await uow.GetRepository<Post>().GetManyQueryable(requestDto?.Specification).FilterListAsync(requestDto?.PagingDto);
                var posts = await lst.List.Select(t => new
                {
                    t.Id,
                    t.Title,
                    t.Slug,
                    t.Summary,
                    t.LikeCount,
                    t.DislikeCount,
                    t.ImageId,
                    t.PublishDate,
                    t.VisibilityType,
                    t.Status,
                    t.RejectionComment,
                    CreationUser = t.CreationUser.FirstName + " " + t.CreationUser.LastName,
                    t.CreationDate,
                }).ToListAsync();

                var ids = posts.Select(t => t.Id);
                var localizedValues = await contentLocalizationService.Value.GetLocalizedValuesAsync(new()
                {
                    ContentIds = ids,
                    ContentType = nameof(Post),
                });

                List<PostsDto> result = new(posts.Count);
                for (var i = 0; i < posts.Count; i++)
                {
                    result.Add(new()
                    {
                        Id = posts[i].Id,
                        DislikeCount = posts[i].DislikeCount,
                        LikeCount = posts[i].LikeCount,
                        Summary = localizedValues.Data?.Find(t => t.ContentId == posts[i].Id && t.Name == nameof(Post.Summary))?.Value ?? posts[i].Summary,
                        Title = localizedValues.Data?.Find(t => t.ContentId == posts[i].Id && t.Name == nameof(Post.Title))?.Value ?? posts[i].Title,
                        Slug = posts[i].Slug,
                        ImageUri = fileService.Value.GetStaticFileUrl(new() { FileId = posts[i].ImageId, ContainerType = ContainerType.Post, }),
                        PublishDate = posts[i].PublishDate,
                        VisibilityType = posts[i].VisibilityType,
                        Status = posts[i].Status,
                        RejectionComment = posts[i].RejectionComment,
                        CreationUser = posts[i].CreationUser,
                        CreationDate = posts[i].CreationDate,
                    });
                }

                return new(OperationResult.Succeeded) { Data = new() { List = result, TotalRecordsCount = lst.TotalRecordsCount } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<IReadOnlyList<KeyValuePair<long, string?>>>> GetPostsTitleAsync([NotNull] ISpecification<Post> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var name = await uow.GetRepository<Post>().GetManyQueryable(specification).Select(t => new KeyValuePair<long, string?>(t.Id, t.Title)).ToListAsync();

                return new(OperationResult.Succeeded) { Data = name };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<PostDto>> GetPostAsync([NotNull] ISpecification<Post> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<Post>();
                var post = await repository.GetManyQueryable(specification).Select(t => new
                {
                    t.Id,
                    t.Title,
                    t.Slug,
                    t.Summary,
                    t.Body,
                    t.ImageId,
                    t.PodcastId,
                    t.LikeCount,
                    t.DislikeCount,
                    t.VisibilityType,
                    CreationUser = t.CreationUser.FirstName + " " + t.CreationUser.LastName,
                    t.CreationUser.AvatarId,
                    t.PublishDate,
                    t.Keywords,
                    t.ViewCount,
                    Tags = t.PostTags == null ? null : t.PostTags.Select(s => new TagDto
                    {
                        Icon = s.Tag.Icon,
                        Id = s.TagId,
                        Name = s.Tag.Name,
                        TagType = s.Tag.TagType,
                    }),
                }).FirstOrDefaultAsync();
                if (post is null)
                {
                    return new(OperationResult.NotFound)
                    {
                        Errors = [new() { Message = Localizer.Value["PostNotFound"] },],
                    };
                }

                var nextId = await repository.GetManyQueryable(new PublishedPostSpecification()).Where(t => t.Id > post.Id).OrderBy(t => t.Id).Select(t => (long?)t.Id).FirstOrDefaultAsync();
                var previousId = await repository.GetManyQueryable(new PublishedPostSpecification()).Where(t => t.Id > post.Id).OrderByDescending(t => t.Id).Select(t => (long?)t.Id).FirstOrDefaultAsync();

                var localizedValues = await contentLocalizationService.Value.GetLocalizedValuesAsync(new()
                {
                    ContentIds = [post.Id],
                    ContentType = nameof(Post),
                });

                var likedByCurrentUser = false;
                var dislikedByCurrentUser = false;
                if (HttpContextAccessor.Value.HttpContext?.User.Identity?.IsAuthenticated == true)
                {
                    var spec = new CategoryTypeEqualsSpecification<Reaction>(CategoryType.Post)
                        .And(new IdentifierIdEqualsSpecification<Reaction>(post.Id))
                        .And(new CreationUserIdEqualsSpecification<Reaction, ApplicationUser, long>(HttpContextAccessor.Value.HttpContext.UserId()));
                    var reaction = await reactionService.Value.GetReactionsAsync(spec);

                    likedByCurrentUser = reaction.Data?.FirstOrDefault()?.Like > 0;
                    dislikedByCurrentUser = reaction.Data?.FirstOrDefault()?.Dislike > 0;
                }

                PostDto result = new()
                {
                    Summary = localizedValues.Data?.Find(t => t.ContentId == post.Id && t.Name == nameof(Post.Summary))?.Value ?? post.Summary,
                    Title = localizedValues.Data?.Find(t => t.ContentId == post.Id && t.Name == nameof(Post.Title))?.Value ?? post.Title,
                    Body = localizedValues.Data?.Find(t => t.ContentId == post.Id && t.Name == nameof(Post.Body))?.Value ?? post.Body,
                    Slug = post.Slug,
                    ImageUri = fileService.Value.GetStaticFileUrl(new() { FileId = post.ImageId, ContainerType = ContainerType.Post, }),
                    PodcastUri = fileService.Value.GetStaticFileUrl(new() { FileId = post.PodcastId, ContainerType = ContainerType.Post, }),
                    LikeCount = post.LikeCount,
                    LikedByCurrentUser = likedByCurrentUser,
                    DislikeCount = post.DislikeCount,
                    DislikedByCurrentUser = dislikedByCurrentUser,
                    CreationUser = post.CreationUser,
                    CreationUserAvatarUri = fileService.Value.GetStaticFileUrl(new() { FileId = post.AvatarId, ContainerType = ContainerType.User, }),
                    Tags = post.Tags,
                    VisibilityType = post.VisibilityType,
                    PublishDate = post.PublishDate,
                    Keywords = post.Keywords,
                    ViewCount = post.ViewCount,
                    NextId = nextId,
                    PreviousId = previousId,
                };

                return new(OperationResult.Succeeded) { Data = result };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<PostEditDto>> GetPostForEditAsync([NotNull] ISpecification<Post> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var post = await uow.GetRepository<Post>().GetManyQueryable(specification).Select(t => new
                {
                    t.Id,
                    t.Title,
                    t.Slug,
                    t.Summary,
                    t.Body,
                    t.ImageId,
                    t.PodcastId,
                    t.Keywords,
                    t.VisibilityType,
                    t.PublishDate,
                    t.Status,
                    t.RejectionComment,
                    Tags = t.PostTags == null ? null : t.PostTags.Select(s => s.TagId).ToList(),
                }).FirstOrDefaultAsync();
                if (post is null)
                {
                    return new(OperationResult.NotFound) { Errors = [new() { Message = Localizer.Value["PostNotFound"] },], };
                }

                // Every language, not just the request's: this feeds an edit form.
                var localized = await uow.GetRepository<ContentLocalization>()
                    .GetManyQueryable(t => t.ContentType == nameof(Post) && t.ContentId == post.Id)
                    .Select(t => new { t.LanguageId, t.Name, t.Value }).ToListAsync();

                return new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        Id = post.Id,
                        Title = post.Title,
                        Slug = post.Slug,
                        Summary = post.Summary,
                        Body = post.Body,
                        ImageUri = fileService.Value.GetStaticFileUrl(new() { FileId = post.ImageId, ContainerType = ContainerType.Post, }),
                        PodcastUri = fileService.Value.GetStaticFileUrl(new() { FileId = post.PodcastId, ContainerType = ContainerType.Post, }),
                        Keywords = post.Keywords,
                        VisibilityType = post.VisibilityType,
                        PublishDate = post.PublishDate,
                        Status = post.Status,
                        RejectionComment = post.RejectionComment,
                        Tags = post.Tags,
                        LocalizedValues = [.. localized.GroupBy(t => t.LanguageId).Select(g => new PostLocalizedValueDto
                        {
                            LanguageId = g.Key,
                            Title = g.FirstOrDefault(t => t.Name == nameof(Post.Title))?.Value,
                            Summary = g.FirstOrDefault(t => t.Name == nameof(Post.Summary))?.Value,
                            Body = g.FirstOrDefault(t => t.Name == nameof(Post.Body))?.Value,
                        })],
                    },
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<long>> ManagePostAsync([NotNull] ManagePostRequestDto requestDto)
        {
            try
            {
                // User-written HTML is cleaned once, here, on the way in: reading it back (and the many v-html sinks in the
                // frontend) then costs nothing. Titles and the like become plain text; the body keeps formatting, tables,
                // formulas and SVG diagrams but loses scripts, event handlers and javascript: links.
                requestDto.Title = requestDto.Title.SanitizePlainText();
                requestDto.Slug = requestDto.Slug.SanitizePlainText();
                requestDto.Summary = requestDto.Summary.SanitizePlainText();
                requestDto.Keywords = requestDto.Keywords.SanitizePlainText();
                requestDto.Body = requestDto.Body.SanitizeHtml();

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<Post>();
                Post? post = null;

                if (requestDto.Id.HasValue)
                {
                    post = await repository.GetAsync(requestDto.Id.Value, includes: (t) => t.Include(s => s.PostTags));
                    if (post is null || (!requestDto.IsAdmin && post.CreationUserId != requestDto.UserId))
                    {
                        return new(OperationResult.NotFound)
                        {
                            Errors = [new() { Message = Localizer.Value["PostNotFound"] },],
                        };
                    }
                }

                if (!string.IsNullOrEmpty(requestDto.Slug))
                {
                    ISpecification<Post> slugSpecification = new SlugEqualsSpecification(requestDto.Slug);
                    if (post is not null)
                    {
                        slugSpecification = slugSpecification.AndNot(new IdEqualsSpecification<Post, long>(post.Id));
                    }

                    var exists = await PostExistsAsync(slugSpecification);
                    if (exists.Data)
                    {
                        return new(OperationResult.Duplicate) { Errors = [new() { Message = Localizer.Value["DuplicateSlug"] },], };
                    }
                }

                if (requestDto.Tags?.Any() == true)
                {
                    var count = await tagService.Value.GetTagsCountAsync(new IdContainsSpecification<Tag, long>(requestDto.Tags));
                    if (count.Data != requestDto.Tags.Count())
                    {
                        return new(OperationResult.Duplicate) { Errors = [new() { Message = Localizer.Value["InvalidTag"] },], };
                    }
                }

                var (imageId, imageErrors) = await SaveFileAsync(requestDto.Image);
                if (imageErrors is not null)
                {
                    return new(OperationResult.Failed) { Errors = imageErrors };
                }

                var (podcastId, podcastErrors) = await SaveFileAsync(requestDto.Podcast);
                if (podcastErrors is not null)
                {
                    return new(OperationResult.Failed) { Errors = podcastErrors };
                }

                var draft = requestDto.Draft.GetValueOrDefault();
                var replacedFileIds = new List<string?>();
                if (post is null)
                {
                    if (imageId is null || requestDto.VisibilityType is null)
                    {
                        return new(OperationResult.NotValid) { Errors = [new() { Message = "Image and VisibilityType are required" },] };
                    }

                    post = new Post
                    {
                        Slug = requestDto.Slug,
                        Title = requestDto.Title,
                        Summary = requestDto.Summary,
                        Body = requestDto.Body,
                        PublishDate = requestDto.PublishDate.GetValueOrDefault(),
                        VisibilityType = requestDto.VisibilityType,
                        Keywords = requestDto.Keywords,
                        ImageId = imageId,
                        PodcastId = podcastId,
                        Status = draft ? Status.Draft : Status.Review,
                        CreationUserId = requestDto.UserId,
                        CreationDate = DateTimeOffset.UtcNow,
                        PostTags = [.. (requestDto.Tags ?? []).Select(t => new PostTag
                        {
                            TagId = t,
                            CreationUserId = requestDto.UserId,
                            CreationDate = DateTimeOffset.UtcNow,
                        })],
                    };
                    repository.Add(post);
                }
                else
                {
                    if (imageId is not null)
                    {
                        replacedFileIds.Add(post.ImageId);
                    }

                    if (requestDto.RemovePodcast || podcastId is not null)
                    {
                        replacedFileIds.Add(post.PodcastId);
                    }

                    post.Slug = requestDto.Slug ?? post.Slug;
                    post.Title = requestDto.Title ?? post.Title;
                    post.Summary = requestDto.Summary ?? post.Summary;
                    post.Body = requestDto.Body ?? post.Body;
                    post.ImageId = imageId ?? post.ImageId;
                    post.PodcastId = requestDto.RemovePodcast ? null : (podcastId ?? post.PodcastId);
                    post.PublishDate = requestDto.PublishDate ?? post.PublishDate;
                    post.VisibilityType = requestDto.VisibilityType ?? post.VisibilityType;
                    post.Keywords = requestDto.Keywords ?? post.Keywords;

                    if (!requestDto.IsAdmin)
                    {
                        // An owner's edit always goes back through moderation; an admin's edit leaves the status alone.
                        post.Status = draft ? Status.Draft : Status.Review;
                        post.RejectionComment = null;
                    }

                    _ = repository.Update(post);

                    if (requestDto.Tags is not null)
                    {
                        var postTagRepository = uow.GetRepository<PostTag>();
                        var existingTags = post.PostTags ?? [];

                        foreach (var item in existingTags.Where(t => !requestDto.Tags.Contains(t.TagId)).ToList())
                        {
                            postTagRepository.Remove(item);
                        }

                        foreach (var item in requestDto.Tags.Where(t => existingTags.All(s => s.TagId != t)))
                        {
                            postTagRepository.Add(new PostTag
                            {
                                PostId = post.Id,
                                TagId = item,
                                CreationDate = DateTimeOffset.UtcNow,
                                CreationUserId = requestDto.UserId,
                            });
                        }
                    }
                }

                _ = await uow.SaveChangesAsync();

                foreach (var fileId in replacedFileIds.Where(t => !string.IsNullOrEmpty(t)))
                {
                    _ = await fileService.Value.RemoveFileAsync(new() { FileId = fileId, ContainerType = ContainerType.Post, });
                }

                await SaveLocalizedValuesAsync(post.Id, requestDto.LocalizedValues);

                if (!requestDto.IsAdmin && !draft)
                {
                    var hasAutoConfirmPost = await identityService.Value.HasClaimAsync(requestDto.UserId, SystemClaim.AutoConfirmPost);
                    if (hasAutoConfirmPost.Data || configuration.Value.GetValue<bool>("AutoConfirmPosts"))
                    {
                        _ = await ConfirmPostAsync(post.Id, false);
                    }
                }

                return new(OperationResult.Succeeded) { Data = post.Id };
            }
            catch (ReferenceConstraintException)
            {
                return new(OperationResult.NotValid) { Errors = [new() { Message = Localizer.Value["InvalidStateId"], }] };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        private async Task SaveLocalizedValuesAsync(long postId, IEnumerable<PostLocalizedValueDto>? localizedValues)
        {
            if (localizedValues is null)
            {
                return;
            }

            foreach (var item in localizedValues)
            {
                foreach (var (name, value) in new[] { (nameof(Post.Title), item.Title), (nameof(Post.Summary), item.Summary), (nameof(Post.Body), item.Body) })
                {
                    _ = await contentLocalizationService.Value.ManageContentLocalizationAsync(new()
                    {
                        ContentId = postId,
                        LanguageId = item.LanguageId,
                        ContentType = nameof(Post),
                        Name = name,
                        Value = name == nameof(Post.Body) ? value.SanitizeHtml() : value.SanitizePlainText(),
                    });
                }
            }
        }

        private async Task AwardContributionPointsAsync(CategoryType categoryType, long identifierId, long userId)
        {
            var points = await applicationSettingsService.Value.GetSettingAsync<long>(categoryType.ApplicationSettingsName);
            if (points.Data > 0)
            {
                _ = await transactionService.Value.IncreaseBalanceAsync(new()
                {
                    Description = $"Successful Contribution - {categoryType.Name}",
                    Points = points.Data,
                    IdentifierId = identifierId,
                    UserId = userId,
                    TransactionType = TransactionType.SuccessfulContribution,
                });
            }
        }

        public async Task<ResultData<bool>> ConfirmPostAsync(long postId, bool notifyUser)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<Post>();

                // Atomic Review -> Confirmed transition: only the caller that wins it awards the points. An admin can also
                // re-approve a Rejected post; that path awards nothing, since a post that was confirmed and then rejected
                // has already been paid for.
                var fromReview = await repository.GetManyQueryable(t => t.Id == postId && t.Status == Status.Review).ExecuteUpdateAsync(t => t
                    .SetProperty(p => p.Status, Status.Confirmed)
                    .SetProperty(p => p.RejectionComment, (string?)null)) > 0;
                if (!fromReview && await repository.GetManyQueryable(t => t.Id == postId && t.Status == Status.Rejected).ExecuteUpdateAsync(t => t
                    .SetProperty(p => p.Status, Status.Confirmed)
                    .SetProperty(p => p.RejectionComment, (string?)null)) == 0)
                {
                    return new(OperationResult.NotFound) { Errors = [new() { Message = Localizer.Value["PostNotFound"] },], };
                }

                var post = await repository.GetManyQueryable(t => t.Id == postId).Select(t => new
                {
                    t.Title,
                    t.CreationUserId,
                    t.CreationUser.Email,
                    FullName = t.CreationUser.FirstName + " " + t.CreationUser.LastName,
                }).FirstAsync();

                if (fromReview)
                {
                    await AwardContributionPointsAsync(CategoryType.Post, postId, post.CreationUserId);
                }

                if (notifyUser && !string.IsNullOrEmpty(post.Email))
                {
                    var template = (await applicationSettingsService.Value.GetSettingAsync<string?>(nameof(ApplicationSettingsDto.PostConfirmationEmailTemplate))).Data;
                    template = template?
                        .Replace("[RECEIVER_NAME]", post.FullName, StringComparison.OrdinalIgnoreCase)
                        .Replace("[POST_TITLE]", post.Title, StringComparison.OrdinalIgnoreCase)
                        .Replace("[POST_ID]", postId.ToString(), StringComparison.OrdinalIgnoreCase);
                    _ = await emailService.Value.SendEmailAsync(new()
                    {
                        Subject = "Post Contribution Confirmation",
                        Body = template!,
                        EmailAddresses = [post.Email],
                    });
                }

                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, },] };
            }
        }

        public async Task<ResultData<bool>> RejectPostAsync(long postId, string? comment, long userId)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var affectedRows = await uow.GetRepository<Post>().GetManyQueryable(t => t.Id == postId && (t.Status == Status.Review || t.Status == Status.Confirmed)).ExecuteUpdateAsync(t => t
                    .SetProperty(p => p.Status, Status.Rejected)
                    .SetProperty(p => p.RejectionComment, comment)
                    .SetProperty(p => p.LastModifyUserId, userId)
                    .SetProperty(p => p.LastModifyDate, DateTimeOffset.UtcNow));
                return affectedRows == 0
                    ? new(OperationResult.NotFound) { Errors = [new() { Message = Localizer.Value["PostNotFound"] },], }
                    : new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, },] };
            }
        }

        public async Task<ResultData<bool>> LikePostAsync([NotNull] PostReactionRequestDto requestDto)
        {
            try
            {
                var specification = new IdEqualsSpecification<Post, long>(requestDto.PostId).And(new PublishedPostSpecification());
                var exists = await PostExistsAsync(specification);
                if (!exists.Data)
                {
                    return new(OperationResult.NotFound)
                    {
                        Errors = [new() { Message = Localizer.Value["InvalidRequest"] },],
                    };
                }

                var reactionResult = await reactionService.Value.ManageReactionAsync(new()
                {
                    CategoryType = CategoryType.Post,
                    CreationDate = DateTimeOffset.UtcNow,
                    CreationUserId = HttpContextAccessor.Value.HttpContext.UserId(),
                    IdentifierId = requestDto.PostId,
                    IsLike = true,
                });
                if (reactionResult.OperationResult is not OperationResult.Succeeded)
                {
                    return new(OperationResult.Failed) { Errors = reactionResult.Errors };
                }

                var result = await UpdatePostReactionsAsync(requestDto.PostId);

                return result;
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, },] };
            }
        }

        public async Task<ResultData<bool>> DislikePostAsync([NotNull] PostReactionRequestDto requestDto)
        {
            try
            {
                var specification = new IdEqualsSpecification<Post, long>(requestDto.PostId).And(new PublishedPostSpecification());
                var exists = await PostExistsAsync(specification);
                if (!exists.Data)
                {
                    return new(OperationResult.NotFound)
                    {
                        Errors = [new() { Message = Localizer.Value["InvalidRequest"] },],
                    };
                }

                var reactionResult = await reactionService.Value.ManageReactionAsync(new()
                {
                    CategoryType = CategoryType.Post,
                    CreationDate = DateTimeOffset.UtcNow,
                    CreationUserId = HttpContextAccessor.Value.HttpContext.UserId(),
                    IdentifierId = requestDto.PostId,
                    IsLike = false,
                });
                if (reactionResult.OperationResult is not OperationResult.Succeeded)
                {
                    return new(OperationResult.Failed) { Errors = reactionResult.Errors };
                }

                var result = await UpdatePostReactionsAsync(requestDto.PostId);

                return result;
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, },] };
            }
        }

        public async Task<ResultData<bool>> RemovePostAsync([NotNull] ISpecification<Post> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var postRepository = uow.GetRepository<Post>();
                var post = await postRepository.GetAsync(specification);
                if (post is null)
                {
                    return new(OperationResult.NotFound) { Errors = [new() { Message = Localizer.Value["PostNotFound"] },], };
                }

                //remove post
                postRepository.Remove(post);
                _ = await uow.SaveChangesAsync();

                //remove reactions
                var reactionSpecification = new IdentifierIdEqualsSpecification<Reaction>(post.Id)
                    .And(new CategoryTypeEqualsSpecification<Reaction>(CategoryType.Post));
                _ = reactionService.Value.RemoveReactionAsync(reactionSpecification);

                //remove image
                _ = await fileService.Value.RemoveFileAsync(new()
                {
                    ContainerType = ContainerType.Post,
                    FileId = post.ImageId,
                });

                //remove Podcast
                _ = await fileService.Value.RemoveFileAsync(new()
                {
                    ContainerType = ContainerType.Post,
                    FileId = post.PodcastId,
                });

                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (ReferenceConstraintException)
            {
                return new(OperationResult.NotValid) { Errors = [new() { Message = Localizer.Value["PostCantBeRemoved"], },] };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, },] };
            }
        }

        public async Task<ResultData<bool>> PostExistsAsync([NotNull] ISpecification<Post> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var exists = await uow.GetRepository<Post>().AnyAsync(specification);

                return new(OperationResult.Succeeded) { Data = exists };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, },] };
            }
        }


        public async Task<ResultData<bool>> IsCreatorOfPostAsync(long postId, long userId)
        {
            try
            {
                var specification = new IdEqualsSpecification<Post, long>(postId)
                    .And(new CreationUserIdEqualsSpecification<Post, ApplicationUser, long>(userId));

                var exists = await PostExistsAsync(specification);

                return new(OperationResult.Succeeded) { Data = exists.Data };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        public async Task IncreasePostViewAsync(long id)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                _ = await uow.GetRepository<Post>().GetManyQueryable(t => t.Id == id).ExecuteUpdateAsync(t => t.SetProperty(p => p.ViewCount, p => p.ViewCount + 1));
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
            }
        }

        private async Task<(string? ImageId, IEnumerable<Error>? Errors)> SaveFileAsync(IFormFile? file)
        {
            if (file is null)
            {
                return (null, null);
            }

            var fileId = await fileService.Value.CreateFileAsync(new()
            {
                File = file,
                ContainerType = ContainerType.Post,
            });

            return (fileId.Data, fileId.Errors);
        }

        #region Comments

        public async Task<ResultData<ListDataSource<PostCommentDto>>> GetPostCommentsAsync(ListRequestDto<PostComment>? requestDto = null)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var data = await uow.GetRepository<PostComment>().GetManyQueryable(requestDto?.Specification).FilterListAsync(requestDto?.PagingDto);
                var lst = await data.List.Select(t => new
                {
                    t.Id,
                    t.PostId,
                    PostTitle = t.Post!.Title,
                    t.Status,
                    t.RejectionComment,
                    t.Comment,
                    CreationUser = t.CreationUser.FirstName + " " + t.CreationUser.LastName,
                    t.CreationUser.AvatarId,
                    t.CreationDate,
                    t.LikeCount,
                    t.DislikeCount,
                }).ToListAsync();
                if (lst is null)
                {
                    return new(OperationResult.Succeeded) { Data = new() { TotalRecordsCount = data.TotalRecordsCount } };
                }


                List<PostCommentDto> result = new(lst.Count);
                List<long> ids = new(lst.Count);
                for (var i = 0; i < lst.Count; i++)
                {
                    result.Add(new()
                    {
                        Id = lst[i].Id,
                        PostId = lst[i].PostId,
                        PostTitle = lst[i].PostTitle,
                        Status = lst[i].Status,
                        RejectionComment = lst[i].RejectionComment,
                        Comment = lst[i].Comment,
                        CreationUser = lst[i].CreationUser,
                        CreationUserAvatarUri = fileService.Value.GetStaticFileUrl(new() { FileId = lst[i].AvatarId, ContainerType = ContainerType.User, }),
                        CreationDate = lst[i].CreationDate,
                        LikeCount = lst[i].LikeCount,
                        DislikeCount = lst[i].DislikeCount,
                    });
                    ids.Add(lst[i].Id);
                }

                if (HttpContextAccessor.Value.HttpContext?.User.Identity?.IsAuthenticated == true)
                {
                    var spec = new CategoryTypeEqualsSpecification<Reaction>(CategoryType.PostComment)
                        .And(new IdentifierIdContainsSpecification<Reaction>(ids))
                        .And(new CreationUserIdEqualsSpecification<Reaction, ApplicationUser, long>(HttpContextAccessor.Value.HttpContext.UserId()));
                    var reactions = await reactionService.Value.GetReactionsAsync(spec);
                    if (reactions.Data is not null)
                    {
                        foreach (var item in reactions.Data)
                        {
                            var reaction = result.Find(t => t.Id == item.IdentifierId);
                            if (reaction is not null)
                            {
                                reaction.LikedByCurrentUser = reaction.LikeCount > 0;
                                reaction.DislikedByCurrentUser = reaction.DislikeCount > 0;
                            }
                        }
                    }
                }

                return new(OperationResult.Succeeded) { Data = new() { List = result, TotalRecordsCount = data.TotalRecordsCount } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<bool>> LikePostCommentAsync([NotNull] PostCommentReactionRequestDto requestDto)
        {
            try
            {
                var specification = new IdEqualsSpecification<PostComment, long>(requestDto.CommentId)
                    .And(new PostIdEqualsSpecification<PostComment>(requestDto.PostId));
                var exists = await CommentExistsAsync(specification);
                if (!exists.Data)
                {
                    return new(OperationResult.NotFound)
                    {
                        Errors = [new() { Message = Localizer.Value["InvalidRequest"] },],
                    };
                }

                var reactionResult = await reactionService.Value.ManageReactionAsync(new()
                {
                    CategoryType = CategoryType.PostComment,
                    CreationDate = DateTimeOffset.UtcNow,
                    CreationUserId = requestDto.UserId,
                    IdentifierId = requestDto.CommentId,
                    IsLike = true,
                });
                if (reactionResult.OperationResult is not OperationResult.Succeeded)
                {
                    return new(OperationResult.Failed) { Errors = reactionResult.Errors };
                }

                var result = await UpdatePostCommentReactionsAsync(requestDto.CommentId);

                return result;
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, },] };
            }
        }

        public async Task<ResultData<bool>> DislikePostCommentAsync([NotNull] PostCommentReactionRequestDto requestDto)
        {
            try
            {
                var specification = new IdEqualsSpecification<PostComment, long>(requestDto.CommentId)
                    .And(new PostIdEqualsSpecification<PostComment>(requestDto.PostId));
                var exists = await CommentExistsAsync(specification);
                if (!exists.Data)
                {
                    return new(OperationResult.NotFound)
                    {
                        Errors = [new() { Message = Localizer.Value["InvalidRequest"] },],
                    };
                }

                var reactionResult = await reactionService.Value.ManageReactionAsync(new()
                {
                    CategoryType = CategoryType.PostComment,
                    CreationDate = DateTimeOffset.UtcNow,
                    CreationUserId = requestDto.UserId,
                    IdentifierId = requestDto.CommentId,
                    IsLike = false,
                });
                if (reactionResult.OperationResult is not OperationResult.Succeeded)
                {
                    return new(OperationResult.Failed) { Errors = reactionResult.Errors };
                }

                var result = await UpdatePostCommentReactionsAsync(requestDto.CommentId);

                return result;
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, },] };
            }
        }

        public async Task<ResultData<long>> CreatePostCommentAsync([NotNull] CreatePostCommentRequestDto requestDto)
        {
            try
            {
                var postExists = await PostExistsAsync(new IdEqualsSpecification<Post, long>(requestDto.PostId).And(new PublishedPostSpecification()));
                if (!postExists.Data)
                {
                    return new(OperationResult.NotFound) { Errors = [new() { Message = Localizer.Value["PostNotFound"] },], };
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<PostComment>();
                var existing = await repository.GetManyQueryable(new PostIdEqualsSpecification<PostComment>(requestDto.PostId)
                    .And(new CreationUserIdEqualsSpecification<PostComment, ApplicationUser, long>(requestDto.UserId)))
                    .Select(t => new { t.Id, t.Status }).FirstOrDefaultAsync();

                long commentId;
                if (existing is null)
                {
                    PostComment comment = new()
                    {
                        PostId = requestDto.PostId,
                        Comment = requestDto.Comment,
                        Status = Status.Review,
                        CreationUserId = requestDto.UserId,
                        CreationDate = DateTimeOffset.UtcNow,
                    };
                    repository.Add(comment);
                    _ = await uow.SaveChangesAsync();
                    commentId = comment.Id;
                }
                else if (existing.Status == Status.Rejected)
                {
                    // PostComments is unique per (user, post), so a rejected comment is resubmitted in place.
                    commentId = existing.Id;
                    _ = await repository.GetManyQueryable(t => t.Id == commentId).ExecuteUpdateAsync(t => t
                        .SetProperty(p => p.Comment, requestDto.Comment)
                        .SetProperty(p => p.Status, Status.Review)
                        .SetProperty(p => p.RejectionComment, (string?)null)
                        .SetProperty(p => p.CreationDate, DateTimeOffset.UtcNow));
                }
                else
                {
                    return new(OperationResult.Failed)
                    {
                        Errors = [new() { Message = existing.Status == Status.Confirmed ? "Comment Exists for Current User and Post" : "there is a pending Comment", }],
                    };
                }

                var hasAutoConfirmPostComment = await identityService.Value.HasClaimAsync(requestDto.UserId, SystemClaim.AutoConfirmPostComment);
                if (hasAutoConfirmPostComment.Data || configuration.Value.GetValue<bool>("AutoConfirmComments"))
                {
                    _ = await ConfirmPostCommentAsync(commentId, false);
                }

                return new(OperationResult.Succeeded) { Data = commentId };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        public async Task<ResultData<bool>> ConfirmPostCommentAsync(long commentId, bool notifyUser)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<PostComment>();

                // Atomic Review -> Confirmed transition: only the caller that wins it awards the points. Re-approving a
                // Rejected comment awards nothing (see ConfirmPostAsync).
                var fromReview = await repository.GetManyQueryable(t => t.Id == commentId && t.Status == Status.Review).ExecuteUpdateAsync(t => t
                    .SetProperty(p => p.Status, Status.Confirmed)
                    .SetProperty(p => p.RejectionComment, (string?)null)) > 0;
                if (!fromReview && await repository.GetManyQueryable(t => t.Id == commentId && t.Status == Status.Rejected).ExecuteUpdateAsync(t => t
                    .SetProperty(p => p.Status, Status.Confirmed)
                    .SetProperty(p => p.RejectionComment, (string?)null)) == 0)
                {
                    return new(OperationResult.NotFound) { Errors = [new() { Message = Localizer.Value["InvalidRequest"] },], };
                }

                var comment = await repository.GetManyQueryable(t => t.Id == commentId).Select(t => new
                {
                    t.PostId,
                    PostTitle = t.Post!.Title,
                    t.Comment,
                    t.CreationUserId,
                    t.CreationUser.Email,
                    FullName = t.CreationUser.FirstName + " " + t.CreationUser.LastName,
                }).FirstAsync();

                if (fromReview)
                {
                    await AwardContributionPointsAsync(CategoryType.PostComment, commentId, comment.CreationUserId);
                }

                if (notifyUser && !string.IsNullOrEmpty(comment.Email))
                {
                    var template = (await applicationSettingsService.Value.GetSettingAsync<string?>(nameof(ApplicationSettingsDto.PostCommentConfirmationEmailTemplate))).Data;
                    template = template?
                        .Replace("[RECEIVER_NAME]", comment.FullName, StringComparison.OrdinalIgnoreCase)
                        .Replace("[POST_TITLE]", comment.PostTitle, StringComparison.OrdinalIgnoreCase)
                        .Replace("[POST_ID]", comment.PostId.ToString(), StringComparison.OrdinalIgnoreCase)
                        .Replace("[COMMENT]", comment.Comment, StringComparison.OrdinalIgnoreCase);
                    _ = await emailService.Value.SendEmailAsync(new()
                    {
                        Subject = "Post Comment Contribution Confirmation",
                        Body = template!,
                        EmailAddresses = [comment.Email],
                    });
                }

                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, },] };
            }
        }

        public async Task<ResultData<bool>> RejectPostCommentAsync(long commentId, string? comment, long userId)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var affectedRows = await uow.GetRepository<PostComment>().GetManyQueryable(t => t.Id == commentId && (t.Status == Status.Review || t.Status == Status.Confirmed)).ExecuteUpdateAsync(t => t
                    .SetProperty(p => p.Status, Status.Rejected)
                    .SetProperty(p => p.RejectionComment, comment)
                    .SetProperty(p => p.LastModifyUserId, userId)
                    .SetProperty(p => p.LastModifyDate, DateTimeOffset.UtcNow));
                return affectedRows == 0
                    ? new(OperationResult.NotFound) { Errors = [new() { Message = Localizer.Value["InvalidRequest"] },], }
                    : new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, },] };
            }
        }

        public async Task<ResultData<bool>> CommentExistsAsync([NotNull] ISpecification<PostComment> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var exists = await uow.GetRepository<PostComment>().AnyAsync(specification);

                return new(OperationResult.Succeeded) { Data = exists };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, },] };
            }
        }

        #endregion

        #region SiteMap

        public ItemType ItemType => ItemType.Post;

        public IQueryable<SiteMapItemDto> GetSiteMapData([NotNull] IUnitOfWork uow)
        {
            var now = DateTimeOffset.UtcNow;
            return uow.GetRepository<Post>().GetManyQueryable(t => t.Status == Status.Confirmed && t.PublishDate <= now && t.VisibilityType == VisibilityType.General).Select(t => new SiteMapItemDto
            {
                Id = t.Id,
                Path1 = t.Id.ToString(),
                Path2 = t.Slug ?? t.Title,
                LastModifyDate = t.LastModifyDate ?? t.CreationDate,
            });
        }

        #endregion

        #region Job

        public async Task<ResultData<bool>> UpdatePostReactionsAsync(long? postId = null)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var where = postId.HasValue ? $" c.Id={postId.Value} AND " : "";
                var query = $@"
;WITH ReactionAgg AS
(
    SELECT r.IdentifierId, SUM(CASE WHEN r.IsLike = 1 THEN 1 ELSE 0 END) AS LikeCount, SUM(CASE WHEN r.IsLike = 0 THEN 1 ELSE 0 END) AS DislikeCount
    FROM Reactions r WHERE r.CategoryType = {CategoryType.Post.Value} GROUP BY r.IdentifierId
)
UPDATE c
SET
    c.LikeCount    = ISNULL(ra.LikeCount, 0),
    c.DislikeCount = ISNULL(ra.DislikeCount, 0)
FROM Posts c
LEFT JOIN ReactionAgg ra ON ra.IdentifierId = c.Id
WHERE {where} (
    c.LikeCount <> ISNULL(ra.LikeCount, 0)
    OR c.DislikeCount <> ISNULL(ra.DislikeCount, 0)
    OR c.LikeCount IS NULL OR c.DislikeCount IS NULL);";

                _ = await uow.ExecuteSqlCommandAsync(query);

                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, },] };
            }
        }

        public async Task<ResultData<bool>> UpdatePostCommentReactionsAsync(long? postCommentId = null)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var where = postCommentId.HasValue ? $" c.Id={postCommentId.Value} AND " : "";
                var query = $@"
;WITH ReactionAgg AS
(
    SELECT r.IdentifierId, SUM(CASE WHEN r.IsLike = 1 THEN 1 ELSE 0 END) AS LikeCount, SUM(CASE WHEN r.IsLike = 0 THEN 1 ELSE 0 END) AS DislikeCount
    FROM Reactions r WHERE r.CategoryType = {CategoryType.PostComment.Value} GROUP BY r.IdentifierId
)
UPDATE c
SET
    c.LikeCount    = ISNULL(ra.LikeCount, 0),
    c.DislikeCount = ISNULL(ra.DislikeCount, 0)
FROM PostComments c
LEFT JOIN ReactionAgg ra ON ra.IdentifierId = c.Id
WHERE {where} (
    c.LikeCount <> ISNULL(ra.LikeCount, 0)
    OR c.DislikeCount <> ISNULL(ra.DislikeCount, 0)
    OR c.LikeCount IS NULL OR c.DislikeCount IS NULL);";

                _ = await uow.ExecuteSqlCommandAsync(query);

                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, },] };
            }
        }

        #endregion
    }
}
