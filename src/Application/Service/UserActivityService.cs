namespace GamaEdtech.Application.Service
{
    using EntityFramework.Exceptions.Common;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.UserActivity;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Domain.Specification.Post;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>
    /// Records reads and podcast listens, built to sit on a busy page: one indexed existence check of the post and one insert,
    /// no read-then-write of the activity itself (the unique index settles a repeat). See docs/business/exams-and-content.md,
    /// "Reads and listens".
    /// </summary>
    public sealed class UserActivityService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor
        , Lazy<IStringLocalizer<UserActivityService>> localizer, Lazy<ILogger<UserActivityService>> logger, Lazy<IConfiguration> configuration)
        : LocalizableServiceBase<UserActivityService>(unitOfWorkProvider, httpContextAccessor, localizer, logger), IUserActivityService
    {
        private const int DefaultRetentionDays = 365;
        private const int MaxDays = 365;
        private const int MaxTopPosts = 100;
        private const int MaxUserItems = 200;

        public async Task<ResultData<bool>> RecordAsync(long userId, UserActivityType type, long postId)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var needsPodcast = type != UserActivityType.PostRead;
                var published = new PublishedPostSpecification().Expression();
                var exists = await uow.GetRepository<Post>().GetManyQueryable(published)
                    .AnyAsync(t => t.Id == postId && (!needsPodcast || t.PodcastId != null));
                if (!exists)
                {
                    return new(OperationResult.NotFound) { Errors = [new() { Message = Localizer.Value["PostNotFound"] }] };
                }

                var now = DateTimeOffset.UtcNow;
                uow.GetRepository<UserActivity>().Add(new()
                {
                    UserId = userId,
                    Type = type,
                    ContentId = postId,
                    Day = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero),
                    CreationDate = now,
                });
                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (UniqueConstraintException)
            {
                // Already recorded today: a refresh or a second tab.
                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<IReadOnlyList<PostActivityDto>>> GetTopPostsAsync(int days, int size)
        {
            try
            {
                var since = Since(days);
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();

                // Per post: different users per kind (a user counts once however many days they came back).
                var top = await uow.GetRepository<UserActivity>().GetManyQueryable(t => t.Day >= since)
                    .GroupBy(t => t.ContentId)
                    .Select(g => new PostActivityDto
                    {
                        PostId = g.Key,
                        Readers = g.Where(t => t.Type == UserActivityType.PostRead).Select(t => t.UserId).Distinct().Count(),
                        Listeners = g.Where(t => t.Type == UserActivityType.PodcastPlay).Select(t => t.UserId).Distinct().Count(),
                        FullListens = g.Where(t => t.Type == UserActivityType.PodcastListen).Select(t => t.UserId).Distinct().Count(),
                    })
                    .OrderByDescending(t => t.Readers).ThenByDescending(t => t.Listeners).ThenByDescending(t => t.PostId)
                    .Take(Math.Clamp(size, 1, MaxTopPosts))
                    .ToListAsync();

                var ids = top.Select(t => t.PostId).ToList();
                var titles = await uow.GetRepository<Post>().GetManyQueryable(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Title);
                foreach (var item in top)
                {
                    item.Title = titles.GetValueOrDefault(item.PostId);
                }

                return new(OperationResult.Succeeded) { Data = top };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<IReadOnlyList<UserActivityItemDto>>> GetUserActivityAsync(long userId, int days)
        {
            try
            {
                var since = Since(days);
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var rows = await uow.GetRepository<UserActivity>().GetManyQueryable(t => t.UserId == userId && t.Day >= since)
                    .OrderByDescending(t => t.CreationDate).Take(MaxUserItems)
                    .Select(t => new { t.Day, t.Type, t.ContentId }).ToListAsync();

                var ids = rows.Select(t => t.ContentId).Distinct().ToList();
                var titles = await uow.GetRepository<Post>().GetManyQueryable(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Title);
                return new(OperationResult.Succeeded)
                {
                    Data = [.. rows.Select(t => new UserActivityItemDto { Day = t.Day, Type = t.Type.Name, PostId = t.ContentId, Title = titles.GetValueOrDefault(t.ContentId) })],
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        private static DateTimeOffset Since(int days) =>
            new(DateTimeOffset.UtcNow.UtcDateTime.Date.AddDays(1 - Math.Clamp(days, 1, MaxDays)), TimeSpan.Zero);

        public async Task<ResultData<int>> DeleteOldAsync()
        {
            try
            {
                var days = Math.Max(configuration.Value.GetValue("UserActivity:RetentionDays", DefaultRetentionDays), 1);
                var before = new DateTimeOffset(DateTimeOffset.UtcNow.UtcDateTime.Date.AddDays(-days), TimeSpan.Zero);
                var deleted = await UnitOfWorkProvider.Value.CreateUnitOfWork().GetRepository<UserActivity>()
                    .GetManyQueryable(t => t.Day < before).ExecuteDeleteAsync();
                return new(OperationResult.Succeeded) { Data = deleted };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }
    }
}
