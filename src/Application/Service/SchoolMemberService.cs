namespace GamaEdtech.Application.Service
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.File;
    using GamaEdtech.Data.Dto.School;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Domain.Specification.School;

    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Identity;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>
    /// Users' connections to schools. Joining also gives the account the matching role (SchoolMemberRole.AccountRoleName);
    /// leaving keeps it, because the role may also have come from an admin or gama-api. See
    /// docs/business/schools-directory.md, "School members".
    /// </summary>
    public class SchoolMemberService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor
        , Lazy<IStringLocalizer<SchoolMemberService>> localizer, Lazy<ILogger<SchoolMemberService>> logger
        , Lazy<UserManager<ApplicationUser>> userManager, Lazy<IFileService> fileService, Lazy<IReferralService> referralService)
        : LocalizableServiceBase<SchoolMemberService>(unitOfWorkProvider, httpContextAccessor, localizer, logger), ISchoolMemberService
    {
        private const int MaxNamedMembers = 50;

        public async Task<ResultData<SchoolMembersDto>> GetSchoolMembersAsync(long schoolId)
        {
            try
            {
                var repository = UnitOfWorkProvider.Value.CreateUnitOfWork().GetRepository<SchoolMember>();
                var schoolSpecification = new SchoolIdEqualsSpecification<SchoolMember>(schoolId);

                var counts = await repository.GetManyQueryable(schoolSpecification).GroupBy(t => t.Role)
                    .Select(t => new SchoolMemberCountDto { Role = t.Key, Count = t.Count() })
                    .ToListAsync();

                var named = await repository.GetManyQueryable(schoolSpecification.And(new NamedSchoolMemberSpecification()))
                    .OrderBy(t => t.Role).ThenBy(t => t.User!.FirstName).ThenBy(t => t.User!.LastName)
                    .Take(MaxNamedMembers)
                    .Select(t => new { t.Role, t.UserId, t.User!.FirstName, t.User.LastName, t.User.AvatarId, t.User.Handle })
                    .ToListAsync();

                return new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        Counts = counts,
                        NamedMembers = named.Select(t => new NamedSchoolMemberDto
                        {
                            Role = t.Role,
                            FullName = $"{t.FirstName} {t.LastName}".Trim(),
                            AvatarUri = fileService.Value.GetStaticFileUrl(new FileUriRequestDto { FileId = t.AvatarId, ContainerType = ContainerType.User }),
                            Handle = string.IsNullOrEmpty(t.Handle) ? $"{t.UserId}-{t.FirstName}-{t.LastName}".ToLowerInvariant() : t.Handle,
                        }).ToList(),
                    },
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<IEnumerable<UserSchoolDto>>> GetUserSchoolsAsync(long userId)
        {
            try
            {
                var lst = await UnitOfWorkProvider.Value.CreateUnitOfWork().GetRepository<SchoolMember>()
                    .GetManyQueryable(t => t.UserId == userId)
                    .OrderByDescending(t => t.CreationDate)
                    .Select(t => new UserSchoolDto
                    {
                        SchoolId = t.SchoolId,
                        SchoolName = t.School!.Name,
                        Role = t.Role,
                        ShowName = t.ShowName,
                        CreationDate = t.CreationDate,
                    })
                    .ToListAsync();

                return new(OperationResult.Succeeded) { Data = lst };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        /// <summary>
        /// Adds the membership, or updates ShowName when the user already has it. Then gives the account the matching
        /// role, and makes the profile Public when the user agreed to be named.
        /// </summary>
        public async Task<ResultData<bool>> JoinSchoolAsync([NotNull] JoinSchoolRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                if (!await uow.GetRepository<School>().AnyAsync(t => t.Id == requestDto.SchoolId))
                {
                    return new(OperationResult.NotFound) { Errors = [new() { Message = Localizer.Value["SchoolNotFound"] },] };
                }

                var showName = requestDto.ShowName && requestDto.Role.CanShowName;
                var repository = uow.GetRepository<SchoolMember>();
                var member = await repository.GetAsync(t => t.UserId == requestDto.UserId && t.SchoolId == requestDto.SchoolId && t.Role == requestDto.Role);
                if (member is null)
                {
                    repository.Add(new SchoolMember
                    {
                        UserId = requestDto.UserId,
                        SchoolId = requestDto.SchoolId,
                        Role = requestDto.Role,
                        ShowName = showName,
                        CreationDate = DateTimeOffset.UtcNow,
                    });
                }
                else
                {
                    member.ShowName = showName;
                    _ = repository.Update(member);
                }

                _ = await uow.SaveChangesAsync();

                await UpdateAccountAsync(requestDto.UserId, requestDto.Role.AccountRoleName(), showName);

                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<bool>> LeaveSchoolAsync(long userId, long schoolId, [NotNull] SchoolMemberRole role)
        {
            try
            {
                var deleted = await UnitOfWorkProvider.Value.CreateUnitOfWork().GetRepository<SchoolMember>()
                    .GetManyQueryable(t => t.UserId == userId && t.SchoolId == schoolId && t.Role == role)
                    .ExecuteDeleteAsync();

                return deleted == 0
                    ? new(OperationResult.NotFound) { Data = false, Errors = [new() { Message = Localizer.Value["MembershipNotFound"] },] }
                    : new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        /// <summary>
        /// Adds the account role and, when the user agreed to be named, makes the profile Public. Only adds, never removes:
        /// a teacher at one school and a parent at another holds both roles. A new teacher gets the invite-program offer,
        /// as when they pick Teacher in their profile.
        /// </summary>
        private async Task UpdateAccountAsync(long userId, string? roleName, bool showName)
        {
            var user = await userManager.Value.FindByIdAsync(userId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (user is null)
            {
                return;
            }

            if (roleName is not null && !await userManager.Value.IsInRoleAsync(user, roleName))
            {
                _ = await userManager.Value.AddToRoleAsync(user, roleName);
                if (roleName == nameof(Role.Teacher))
                {
                    _ = await referralService.Value.CreateOfferAsync(userId);
                }
            }

            if (showName && user.ProfileVisibility != ProfileVisibility.Public)
            {
                user.ProfileVisibility = ProfileVisibility.Public;
                _ = await userManager.Value.UpdateAsync(user);
            }
        }
    }
}
