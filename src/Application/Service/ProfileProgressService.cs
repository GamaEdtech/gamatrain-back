namespace GamaEdtech.Application.Service
{
    using System;
    using System.Linq;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.ProfileProgress;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>
    /// The signed-in user's progress for the profile page. Read-only and built from data already stored: profile fields,
    /// points, practice answers (<c>TestSubmission</c>), the user's school, its members and contributions. See
    /// docs/business/identity-and-access.md, "Profile progress".
    /// </summary>
    public class ProfileProgressService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor
        , Lazy<IStringLocalizer<ProfileProgressService>> localizer, Lazy<ILogger<ProfileProgressService>> logger
        , Lazy<IApplicationSettingsService> applicationSettingsService)
        : LocalizableServiceBase<ProfileProgressService>(unitOfWorkProvider, httpContextAccessor, localizer, logger), IProfileProgressService
    {
        public async Task<ResultData<ProfileProgressDto>> GetProgressAsync(long userId)
        {
            try
            {
                // Each repository call opens its own context (see CLAUDE.md), and these are all reads.
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var user = await uow.GetRepository<ApplicationUser>().GetManyQueryable(t => t.Id == userId)
                    .Select(t => new
                    {
                        t.AvatarId,
                        t.FirstName,
                        t.LastName,
                        t.CurrentStatusSentence,
                        t.Biography,
                        t.Skills,
                        t.CurrentBalance,
                        t.SchoolId,
                        IsAdmin = t.UserRoles!.Any(r => r.Role!.Name == nameof(Role.Admin)),
                    })
                    .FirstOrDefaultAsync();
                if (user is null)
                {
                    return new(OperationResult.NotFound) { Errors = [new() { Message = Localizer.Value["UserNotFound"] },] };
                }

                var hasExperience = await uow.GetRepository<Experience>().GetManyQueryable(t => t.UserId == userId).AnyAsync();
                var skills = user.Skills?.Split(Delimiter, StringSplitOptions.RemoveEmptyEntries);
                IEnumerable<long> experienceIds = hasExperience ? [1L] : [];
                var percent = UserRateLevel.Score(user.AvatarId, user.FirstName, user.LastName, user.CurrentStatusSentence, user.Biography, skills, experienceIds);
                var (nextLevel, nextAt) = ProfileProgressRules.NextLevel(percent);

                // Same population as the leaderboard (GetTop100UsersAsync): everyone but admins.
                int? rank = user.IsAdmin
                    ? null
                    : await uow.GetRepository<ApplicationUser>().GetManyQueryable(t => t.CurrentBalance > user.CurrentBalance
                        && !t.UserRoles!.Any(r => r.Role!.Name == nameof(Role.Admin))).CountAsync() + 1;

                var days = await uow.GetRepository<TestSubmission>().GetManyQueryable(t => t.UserId == userId)
                    .Select(t => t.CreationDate.UtcDateTime.Date).Distinct().ToListAsync();
                var today = DateOnly.FromDateTime(DateTime.UtcNow);

                return new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        Level = UserRateLevel.Calculate(percent),
                        Percent = percent,
                        NextLevel = nextLevel,
                        NextLevelAtPercent = nextAt,
                        Quests = ProfileProgressRules.Quests(user.AvatarId, user.FirstName, user.LastName, user.CurrentStatusSentence, user.Biography, skills, hasExperience),
                        Points = user.CurrentBalance,
                        Rank = rank,
                        Practice = ProfileProgressRules.Streak(days.Select(DateOnly.FromDateTime), today),
                        School = await GetSchoolAsync(userId, user.SchoolId),
                    },
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        /// <summary>The user's review as the page shows it: approved, waiting (draft or in review), or none (also when rejected).</summary>
        private static Status? ReviewState(Status? status) => status switch
        {
            _ when status == Status.Confirmed => Status.Confirmed,
            _ when status == Status.Draft || status == Status.Review => Status.Review,
            _ => null,
        };

        /// <summary>
        /// The school on the profile, else the one most recently joined as a member. A hidden (deleted) school counts as
        /// none, so the page never links to a school page that doesn't exist.
        /// </summary>
        private async Task<ProfileSchoolDto?> GetSchoolAsync(long userId, long? profileSchoolId)
        {
            var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
            var schoolId = profileSchoolId ?? await uow.GetRepository<SchoolMember>().GetManyQueryable(t => t.UserId == userId)
                .OrderByDescending(t => t.CreationDate).Select(t => (long?)t.SchoolId).FirstOrDefaultAsync();
            if (schoolId is null)
            {
                return null;
            }

            var school = await uow.GetRepository<School>().GetManyQueryable(t => t.Id == schoolId && !t.IsDeleted)
                .Select(t => new { t.Id, t.Name, Reviews = t.CommentsRatingCount ?? 0, Photos = t.SchoolImages.Count })
                .FirstOrDefaultAsync();
            if (school is null)
            {
                return null;
            }

            var members = await uow.GetRepository<SchoolMember>().GetManyQueryable(t => t.SchoolId == school.Id)
                .GroupBy(t => t.Role).Select(t => new { Role = t.Key, Count = t.Count() }).ToListAsync();

            var monthStart = new DateTimeOffset(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);
            // The contributions that improve one school; their IdentifierId is the school's id (RemoveSchoolImage's is the image's).
            var contributions = uow.GetRepository<Contribution>().GetManyQueryable(t => t.IdentifierId == school.Id
                && (t.CategoryType == CategoryType.School || t.CategoryType == CategoryType.SchoolComment
                    || t.CategoryType == CategoryType.SchoolImage || t.CategoryType == CategoryType.SchoolIssues));

            // Submitted this month and approved since: the save hook doesn't stamp LastModifyDate on Contribution (see
            // CLAUDE.md), so the approval date itself isn't stored.
            var helpers = await contributions.Where(t => t.Status == Status.Confirmed && t.CreationDate >= monthStart)
                .Select(t => t.CreationUserId).Distinct().CountAsync();

            var myReview = await contributions.Where(t => t.CreationUserId == userId && t.CategoryType == CategoryType.SchoolComment)
                .OrderByDescending(t => t.CreationDate).Select(t => t.Status).FirstOrDefaultAsync();

            var settings = (await applicationSettingsService.Value.GetApplicationSettingsAsync()).Data;

            return new()
            {
                Id = school.Id,
                Name = school.Name,
                Reviews = school.Reviews,
                ReviewGoal = ProfileProgressRules.NextGoal(school.Reviews),
                Photos = school.Photos,
                Students = members.Where(t => t.Role == SchoolMemberRole.Student).Sum(t => t.Count),
                Teachers = members.Where(t => t.Role == SchoolMemberRole.Teacher).Sum(t => t.Count),
                HelpersThisMonth = helpers,
                MyReview = ReviewState(myReview),
                ReviewPoints = settings?.SchoolCommentContributionPoints ?? 0,
                PhotoPoints = settings?.SchoolImageContributionPoints ?? 0,
                EditPoints = settings?.SchoolContributionPoints ?? 0,
            };
        }
    }
}
