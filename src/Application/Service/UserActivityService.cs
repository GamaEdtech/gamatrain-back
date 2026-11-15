namespace GamaEdtech.Application.Service
{
    using EntityFramework.Exceptions.Common;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
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
