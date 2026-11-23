namespace GamaEdtech.Application.Service
{
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.ExamReview;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    /// <inheritdoc cref="IExamReviewService"/>
    public sealed class ExamReviewService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor,
        Lazy<ILogger<ExamReviewService>> logger, Lazy<IFileService> fileService)
        : ServiceBase<ExamReviewService>(unitOfWorkProvider, httpContextAccessor, logger), IExamReviewService
    {
        // gama-api's statuses an exam is public in: 1 published, 5 cloned, 7 published by its teacher.
        private static readonly byte[] PublicStatuses = [1, 5, 7];

        /// <summary>A finished attempt (gama-api's azmoon_data status 1).</summary>
        private const byte FinishedAttempt = 1;

        private const int MaxPageSize = 50;
        private const int MaxAdminPageSize = 100;

        public async Task<ResultData<ExamReviewsDto>> GetReviewsAsync(long examId, long? userId, int page, int pageSize)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var confirmed = uow.GetRepository<ExamReview>().GetManyQueryable(t => t.ExamId == examId && t.Status == Status.Confirmed);
                var counts = await confirmed.GroupBy(t => t.Rating).Select(t => new { Rating = t.Key, Count = t.Count() }).ToListAsync();
                var distribution = Enumerable.Range(1, 5).Select(r => counts.FirstOrDefault(t => t.Rating == r)?.Count ?? 0).ToArray();
                var count = distribution.Sum();

                var withComment = confirmed.Where(t => t.Comment != null && t.Comment != string.Empty);
                var size = Math.Clamp(pageSize, 1, MaxPageSize);
                var commentCount = await withComment.CountAsync();
                var items = await withComment.OrderByDescending(t => t.CreationDate).ThenByDescending(t => t.Id)
                    .Skip((Math.Max(page, 1) - 1) * size).Take(size)
                    .Select(t => new { t.Id, t.Rating, t.Comment, t.Status, t.RejectionComment, Date = t.LastModifyDate ?? t.CreationDate, t.User!.FirstName, t.User.LastName, t.User.AvatarId })
                    .ToListAsync();

                ExamReviewsDto data = new()
                {
                    Count = count,
                    Average = count == 0 ? null : Math.Round((decimal)distribution.Select((n, i) => n * (i + 1)).Sum() / count, 1),
                    Distribution = distribution,
                    CommentCount = commentCount,
                    Items = [.. items.Select(t => Item(t.Id, t.Rating, t.Comment, t.Status, null, t.Date, t.FirstName, t.LastName, t.AvatarId))],
                };

                if (userId is long caller and > 0)
                {
                    var mine = await uow.GetRepository<ExamReview>().GetManyQueryable(t => t.ExamId == examId && t.UserId == caller)
                        .Select(t => new { t.Id, t.Rating, t.Comment, t.Status, t.RejectionComment, Date = t.LastModifyDate ?? t.CreationDate, t.User!.FirstName, t.User.LastName, t.User.AvatarId })
                        .FirstOrDefaultAsync();
                    data.Mine = mine is null ? null : Item(mine.Id, mine.Rating, mine.Comment, mine.Status, mine.RejectionComment, mine.Date, mine.FirstName, mine.LastName, mine.AvatarId);
                    data.CanReview = await HasFinishedAsync(uow, caller, examId);
                }

                return new(OperationResult.Succeeded) { Data = data };
            }
            catch (Exception exc)
            {
                return Error<ExamReviewsDto>(exc);
            }
        }

        public async Task<ResultData<ExamReviewItemDto>> SaveReviewAsync([NotNull] SaveExamReviewRequestDto requestDto)
        {
            try
            {
                if (requestDto.Rating is < 1 or > 5)
                {
                    return Invalid<ExamReviewItemDto>("rating", "The rating is 1 to 5.");
                }

                var comment = string.IsNullOrWhiteSpace(requestDto.Comment) ? null : requestDto.Comment.Trim();
                if (comment?.Length > ExamReview.MaxCommentLength)
                {
                    return Invalid<ExamReviewItemDto>("comment", $"The comment is at most {ExamReview.MaxCommentLength} characters.");
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                if (!await uow.GetRepository<Exam>().AnyAsync(t => t.Id == requestDto.ExamId && !t.IsDeleted && PublicStatuses.Contains(t.Status)))
                {
                    return Invalid<ExamReviewItemDto>("noResult", "No such exam.");
                }

                // Only someone who took the exam reviews it: honest ratings, and no drive-by spam.
                if (!await HasFinishedAsync(uow, requestDto.UserId, requestDto.ExamId))
                {
                    return Invalid<ExamReviewItemDto>("notParticipated", "Finish the exam to review it.");
                }

                var repository = uow.GetRepository<ExamReview>();
                var review = await repository.GetManyQueryable(t => t.ExamId == requestDto.ExamId && t.UserId == requestDto.UserId, tracking: true).FirstOrDefaultAsync();
                var now = DateTimeOffset.UtcNow;
                if (review is null)
                {
                    review = new() { ExamId = requestDto.ExamId, UserId = requestDto.UserId, CreationDate = now };
                    repository.Add(review);
                }
                else
                {
                    review.LastModifyDate = now;
                }

                // Public at once, like a school's comments. One an admin hid stays hidden: editing it doesn't undo that.
                review.Rating = requestDto.Rating;
                review.Comment = comment;
                if (review.Status != Status.Rejected)
                {
                    review.Status = Status.Confirmed;
                }

                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = Item(review.Id, review.Rating, review.Comment, review.Status, review.RejectionComment, now, null, null, null) };
            }
            catch (DbUpdateException exc)
            {
                // Two saves at once for a new review: the unique (exam, user) index keeps one; the other is told to retry.
                Logger.Value.LogWarning(exc, "Exam review not saved (exam {ExamId})", requestDto.ExamId);
                return Invalid<ExamReviewItemDto>("conflict", "Please try again.");
            }
            catch (Exception exc)
            {
                return Error<ExamReviewItemDto>(exc);
            }
        }

        public async Task<ResultData<bool>> DeleteReviewAsync(long userId, long examId)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<ExamReview>();
                var review = await repository.GetManyQueryable(t => t.ExamId == examId && t.UserId == userId, tracking: true).FirstOrDefaultAsync();
                if (review is null)
                {
                    return new(OperationResult.Succeeded) { Data = false };
                }

                repository.Remove(review);
                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                return Error<bool>(exc);
            }
        }

        public async Task<ResultData<ExamReviewAdminListDto>> GetAdminReviewsAsync([NotNull] ExamReviewAdminListRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var query = uow.GetRepository<ExamReview>().GetManyQueryable(t => true);
                if (requestDto.Status is { } status)
                {
                    query = query.Where(t => t.Status == status);
                }

                if (requestDto.ExamId is long examId)
                {
                    query = query.Where(t => t.ExamId == examId);
                }

                if (requestDto.WithComment is bool withComment)
                {
                    query = withComment ? query.Where(t => t.Comment != null && t.Comment != string.Empty) : query.Where(t => t.Comment == null || t.Comment == string.Empty);
                }

                var size = Math.Clamp(requestDto.PageSize, 1, MaxAdminPageSize);
                var total = await query.CountAsync();
                var items = await query.OrderByDescending(t => t.LastModifyDate ?? t.CreationDate).ThenByDescending(t => t.Id)
                    .Skip((Math.Max(requestDto.Page, 1) - 1) * size).Take(size)
                    .Select(t => new ExamReviewAdminItemDto
                    {
                        Id = t.Id,
                        ExamId = t.ExamId,
                        ExamTitle = t.Exam!.Title,
                        UserId = t.UserId,
                        UserName = (t.User!.FirstName + " " + t.User.LastName).Trim(),
                        Rating = t.Rating,
                        Comment = t.Comment,
                        Status = t.Status,
                        RejectionComment = t.RejectionComment,
                        CreationDate = t.CreationDate,
                        LastModifyDate = t.LastModifyDate,
                    })
                    .ToListAsync();
                return new(OperationResult.Succeeded) { Data = new() { Total = total, Items = items } };
            }
            catch (Exception exc)
            {
                return Error<ExamReviewAdminListDto>(exc);
            }
        }

        public async Task<ResultData<bool>> ModerateAsync(long id, bool confirm, string? rejectionComment)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var review = await uow.GetRepository<ExamReview>().GetManyQueryable(t => t.Id == id, tracking: true).FirstOrDefaultAsync();
                if (review is null)
                {
                    return Invalid<bool>("notFound", "No such review.");
                }

                review.Status = confirm ? Status.Confirmed : Status.Rejected;
                var note = confirm || string.IsNullOrWhiteSpace(rejectionComment) ? null : rejectionComment.Trim();
                review.RejectionComment = note?[..Math.Min(note.Length, 300)];
                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                return Error<bool>(exc);
            }
        }

        private static Task<bool> HasFinishedAsync(IUnitOfWork uow, long userId, long examId) =>
            uow.GetRepository<ExamAttempt>().AnyAsync(t => t.UserId == userId && t.ExamId == examId && t.Status == FinishedAttempt);

        private ExamReviewItemDto Item(long id, byte rating, string? comment, Status status, string? rejection, DateTimeOffset date, string? firstName, string? lastName, string? avatarId) => new()
        {
            Id = id,
            Name = ShortName(firstName, lastName),
            AvatarUrl = string.IsNullOrEmpty(avatarId) ? null : fileService.Value.GetStaticFileUrl(new() { FileId = avatarId, ContainerType = ContainerType.User }),
            Rating = rating,
            Comment = comment,
            Status = status,
            RejectionComment = rejection,
            Date = date,
        };

        /// <summary>"Ana L.": the first name and the last name's initial, never more of who wrote it.</summary>
        private static string? ShortName(string? firstName, string? lastName)
        {
            var first = firstName?.Trim();
            var initial = string.IsNullOrWhiteSpace(lastName) ? null : $"{char.ToUpperInvariant(lastName.Trim()[0])}.";
            var name = string.Join(' ', new[] { first, initial }.Where(t => !string.IsNullOrEmpty(t)));
            return name.Length == 0 ? null : name;
        }

        private static ResultData<T> Invalid<T>(string reference, string message) => new(OperationResult.NotValid) { Errors = [new() { Message = message, Reference = reference }] };

        private ResultData<T> Error<T>(Exception exc)
        {
            Logger.Value.LogException(exc);
            return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
        }
    }
}
