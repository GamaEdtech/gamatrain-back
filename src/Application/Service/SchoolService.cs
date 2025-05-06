namespace GamaEdtech.Application.Service
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;
    using System.Text.Json;

    using EntityFramework.Exceptions.Common;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Core.Extensions.Linq;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Common.DataAccess.Specification.Impl;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.Contribution;
    using GamaEdtech.Data.Dto.School;
    using GamaEdtech.Data.Dto.Tag;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Domain.Specification;
    using GamaEdtech.Domain.Specification.School;
    using GamaEdtech.Domain.Specification.Tag;

    using MetadataExtractor;
    using MetadataExtractor.Formats.Exif;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using NetTopologySuite;
    using NetTopologySuite.Geometries;

    using static GamaEdtech.Common.Core.Constants;

    public class SchoolService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor, Lazy<IStringLocalizer<FileService>> localizer
        , Lazy<ILogger<FileService>> logger, Lazy<IFileService> fileService, Lazy<IContributionService> contributionService, Lazy<IIdentityService> identityService
        , Lazy<IConfiguration> configuration, Lazy<ITagService> tagService, Lazy<IReactionService> reactionService)
        : LocalizableServiceBase<FileService>(unitOfWorkProvider, httpContextAccessor, localizer, logger), ISchoolService
    {
        #region Schools

        public async Task<ResultData<ListDataSource<SchoolsDto>>> GetSchoolsAsync(ListRequestDto<School>? requestDto = null)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var result = await uow.GetRepository<School, long>().GetManyQueryable(requestDto?.Specification).FilterListAsync(requestDto?.PagingDto);
                var users = await result.List.Select(t => new SchoolsDto
                {
                    Id = t.Id,
                    Name = t.Name,
                    LocalName = t.LocalName,
                }).ToListAsync();
                return new(OperationResult.Succeeded) { Data = new() { List = users, TotalRecordsCount = result.TotalRecordsCount } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<ListDataSource<SchoolInfoDto>>> GetSchoolsListAsync(ListRequestDto<School>? requestDto = null, Point? point = null)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var lst = uow.GetRepository<School, long>().GetManyQueryable(requestDto?.Specification);
                int? total = requestDto?.PagingDto?.PageFilter?.ReturnTotalRecordsCount == true ? await lst.CountAsync() : null;
                var query = lst.Select(t => new SchoolInfoDto
                {
                    Id = t.Id,
                    Name = t.Name,
                    LastModifyDate = t.LastModifyDate ?? t.CreationDate,
                    HasWebSite = !string.IsNullOrEmpty(t.WebSite),
                    HasEmail = !string.IsNullOrEmpty(t.Email),
                    HasPhoneNumber = !string.IsNullOrEmpty(t.PhoneNumber),
                    Coordinates = t.Coordinates,
                    Score = t.Score,
                    CityTitle = t.City == null ? "" : t.City.Title,
                    CountryTitle = t.Country == null ? "" : t.Country.Title,
                    StateTitle = t.State == null ? "" : t.State.Title,
                    Distance = point != null && t.Coordinates != null ? t.Coordinates!.Distance(point) : null,
                });

                (query, var sortApplied) = query.OrderBy(requestDto?.PagingDto?.SortFilter);
                if (!sortApplied)
                {
                    query = point is not null ? query.OrderBy(t => t.Distance) : query.OrderBy(t => t.Id);
                }
                if (requestDto?.PagingDto?.PageFilter is not null)
                {
                    query = query.Skip(requestDto.PagingDto.PageFilter.Skip)
                        .Take(requestDto.PagingDto.PageFilter.Size);
                }
                var result = await query.ToListAsync();

                return new(OperationResult.Succeeded) { Data = new() { List = result, TotalRecordsCount = total } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<SchoolDto>> GetSchoolAsync([NotNull] ISpecification<School> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var school = await uow.GetRepository<School, long>().GetManyQueryable(specification).Select(t => new SchoolDto
                {
                    Id = t.Id,
                    Name = t.Name,
                    LocalName = t.LocalName,
                    Address = t.Address,
                    LocalAddress = t.LocalAddress,
                    Coordinates = t.Coordinates,
                    SchoolType = t.SchoolType,
                    ZipCode = t.ZipCode,
                    CityId = t.CityId,
                    CityTitle = t.City != null ? t.City.Title : "",
                    CountryId = t.CountryId,
                    CountryTitle = t.Country != null ? t.Country.Title : "",
                    StateId = t.StateId,
                    StateTitle = t.State != null ? t.State.Title : "",
                    WebSite = t.WebSite,
                    FaxNumber = t.FaxNumber,
                    PhoneNumber = t.PhoneNumber,
                    Email = t.Email,
                    Quarter = t.Quarter,
                    OsmId = t.OsmId,
                    Tags = t.SchoolTags == null ? null : t.SchoolTags.Select(s => new TagDto
                    {
                        Icon = s.Tag.Icon,
                        Id = s.TagId,
                        Name = s.Tag.Name,
                        TagType = s.Tag.TagType,
                    }),
                }).FirstOrDefaultAsync();

                return school is null
                    ? new(OperationResult.NotFound)
                    {
                        Errors = [new() { Message = Localizer.Value["SchoolNotFound"] },],
                    }
                    : new(OperationResult.Succeeded) { Data = school };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<IReadOnlyList<KeyValuePair<long, string?>>>> GetSchoolsNameAsync([NotNull] ISpecification<School> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var name = await uow.GetRepository<School>().GetManyQueryable(specification).Select(t => new KeyValuePair<long, string?>(t.Id, t.Name)).ToListAsync();

                return new(OperationResult.Succeeded) { Data = name };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<long>> ManageSchoolAsync([NotNull] ManageSchoolRequestDto requestDto, bool ignoreNullValues)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<School, long>();
                School? school = null;

                if (requestDto.Id.HasValue)
                {
                    school = await repository.GetAsync(requestDto.Id.Value, includes: (t) => t.Include(s => s.SchoolTags));
                    if (school is null)
                    {
                        return new(OperationResult.NotFound)
                        {
                            Errors = [new() { Message = Localizer.Value["SchoolNotFound"] },],
                        };
                    }

                    school.Name = Get(requestDto.Name, school.Name);
                    school.LocalName = Get(requestDto.LocalName, school.LocalName);
                    school.Address = Get(requestDto.Address, school.Address);
                    school.Coordinates = Get(requestDto.Coordinates, school.Coordinates);
                    school.SchoolType = Get(requestDto.SchoolType, school.SchoolType);
                    school.StateId = Get(requestDto.StateId, school.StateId);
                    school.ZipCode = Get(requestDto.ZipCode, school.ZipCode);
                    school.WebSite = Get(requestDto.WebSite, school.WebSite);
                    school.Quarter = Get(requestDto.Quarter, school.Quarter);
                    school.PhoneNumber = Get(requestDto.PhoneNumber, school.PhoneNumber);
                    school.LocalAddress = Get(requestDto.LocalAddress, school.LocalAddress);
                    school.FaxNumber = Get(requestDto.FaxNumber, school.FaxNumber);
                    school.Email = Get(requestDto.Email, school.Email);
                    school.CityId = Get(requestDto.CityId, school.CityId);
                    school.CountryId = Get(requestDto.CountryId, school.CountryId);
                    school.OsmId = Get(requestDto.OsmId, school.OsmId);
                    school.LastModifyDate = requestDto.Date;
                    school.LastModifyUserId = requestDto.UserId;

                    _ = repository.Update(school);

                    if (!ignoreNullValues || requestDto.Tags?.Any() == true)
                    {
                        var schoolTagRepository = uow.GetRepository<SchoolTag>();

                        var removedTags = school.SchoolTags?.Where(t => requestDto.Tags is null || !requestDto.Tags.Contains(t.TagId));
                        var newTags = requestDto.Tags?.Where(t => school.SchoolTags is null || school.SchoolTags.All(s => s.TagId != t));

                        if (removedTags is not null)
                        {
                            foreach (var item in removedTags)
                            {
                                schoolTagRepository.Remove(item);
                            }
                        }

                        if (newTags is not null)
                        {
                            foreach (var item in newTags)
                            {
                                schoolTagRepository.Add(new SchoolTag
                                {
                                    SchoolId = requestDto.Id.Value,
                                    TagId = item,
                                    CreationDate = requestDto.Date,
                                    CreationUserId = requestDto.UserId,
                                });
                            }
                        }
                    }
                }
                else
                {
                    school = new School
                    {
                        Name = requestDto.Name,
                        LocalName = requestDto.LocalName,
                        Address = requestDto.Address,
                        Coordinates = requestDto.Coordinates,
                        SchoolType = requestDto.SchoolType,
                        StateId = requestDto.StateId,
                        ZipCode = requestDto.ZipCode,
                        WebSite = requestDto.WebSite,
                        Quarter = requestDto.Quarter,
                        PhoneNumber = requestDto.PhoneNumber,
                        LocalAddress = requestDto.LocalAddress,
                        FaxNumber = requestDto.FaxNumber,
                        Email = requestDto.Email,
                        CityId = requestDto.CityId,
                        CountryId = requestDto.CountryId,
                        OsmId = requestDto.OsmId,
                    };
                    if (requestDto.Tags is not null)
                    {
                        school.SchoolTags = [.. requestDto.Tags.Select(t => new SchoolTag {
                            TagId = t,
                            CreationUserId=requestDto.UserId,
                            CreationDate=requestDto.Date,
                        })];
                    }
                    repository.Add(school);
                }

                _ = await uow.SaveChangesAsync();

                return new(OperationResult.Succeeded) { Data = school.Id };

                T Get<T>(T newValue, T oldValue)
                {
                    if (!ignoreNullValues)
                    {
                        return newValue;
                    }

                    var isEmpty = string.IsNullOrEmpty(newValue?.ToString());
                    return isEmpty ? oldValue : newValue;
                }
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

        public async Task<ResultData<bool>> RemoveSchoolAsync([NotNull] ISpecification<School> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var school = await uow.GetRepository<School, long>().GetAsync(specification);
                if (school is null)
                {
                    return new(OperationResult.NotFound)
                    {
                        Data = false,
                        Errors = [new() { Message = Localizer.Value["SchoolNotFound"] },],
                    };
                }

                uow.GetRepository<School, long>().Remove(school);
                _ = await uow.SaveChangesAsync();
                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (ReferenceConstraintException)
            {
                return new(OperationResult.NotValid) { Errors = [new() { Message = Localizer.Value["SchoolCantBeRemoved"], },] };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, },] };
            }
        }

        public async Task<ResultData<bool>> ExistsSchoolAsync([NotNull] ISpecification<School> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var exists = await uow.GetRepository<School, long>().AnyAsync(specification);
                return new(OperationResult.Succeeded) { Data = exists };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        #endregion

        #region Rate and Comments

        public async Task<ResultData<SchoolRateDto>> GetSchoolRateAsync([NotNull] ISpecification<SchoolComment> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var result = await uow.GetRepository<SchoolComment>().GetManyQueryable(specification)
                    .GroupBy(t => t.SchoolId)
                    .Select(t => new SchoolRateDto
                    {
                        AverageRate = t.Average(c => c.AverageRate),
                        TuitionRatioRate = t.Average(c => c.TuitionRatioRate),
                        SafetyAndHappinessRate = t.Average(c => c.SafetyAndHappinessRate),
                        ITTrainingRate = t.Average(c => c.ITTrainingRate),
                        FacilitiesRate = t.Average(c => c.FacilitiesRate),
                        EducationRate = t.Average(c => c.EducationRate),
                        ClassesQualityRate = t.Average(c => c.ClassesQualityRate),
                        BehaviorRate = t.Average(c => c.BehaviorRate),
                        ArtisticActivitiesRate = t.Average(c => c.ArtisticActivitiesRate),
                        TotalCount = t.Count(),
                    }).FirstOrDefaultAsync();

                return new(OperationResult.Succeeded) { Data = result };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<ListDataSource<SchoolCommentDto>>> GetSchoolCommentsAsync(ListRequestDto<SchoolComment>? requestDto = null)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var result = await uow.GetRepository<SchoolComment>().GetManyQueryable(requestDto?.Specification).FilterListAsync(requestDto?.PagingDto);
                var users = await result.List.Select(t => new SchoolCommentDto
                {
                    Id = t.Id,
                    Comment = t.Comment,
                    CreationUser = t.CreationUser!.FirstName + " " + t.CreationUser.LastName,
                    CreationUserAvatar = t.CreationUser!.Avatar,
                    CreationDate = t.CreationDate,
                    LikeCount = t.LikeCount,
                    DislikeCount = t.DislikeCount,
                    AverageRate = t.AverageRate,
                }).ToListAsync();
                return new(OperationResult.Succeeded) { Data = new() { List = users, TotalRecordsCount = result.TotalRecordsCount } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<bool>> LikeSchoolCommentAsync([NotNull] SchoolCommentReactionRequestDto requestDto)
        {
            try
            {
                var specification = new IdEqualsSpecification<SchoolComment, long>(requestDto.CommentId)
                    .And(new SchoolIdEqualsSpecification<SchoolComment>(requestDto.SchoolId));
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
                    CategoryType = CategoryType.SchoolComment,
                    CreationDate = DateTimeOffset.UtcNow,
                    CreationUserId = HttpContextAccessor.Value.HttpContext.UserId(),
                    IdentifierId = requestDto.CommentId,
                    IsLike = true,
                });
                if (reactionResult.OperationResult is not OperationResult.Succeeded)
                {
                    return new(OperationResult.Failed) { Errors = reactionResult.Errors };
                }

                var result = await UpdateSchoolCommentReactionsAsync(requestDto.CommentId);

                return result;
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, },] };
            }
        }

        public async Task<ResultData<bool>> DislikeSchoolCommentAsync([NotNull] SchoolCommentReactionRequestDto requestDto)
        {
            try
            {
                var specification = new IdEqualsSpecification<SchoolComment, long>(requestDto.CommentId)
                    .And(new SchoolIdEqualsSpecification<SchoolComment>(requestDto.SchoolId));
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
                    CategoryType = CategoryType.SchoolComment,
                    CreationDate = DateTimeOffset.UtcNow,
                    CreationUserId = HttpContextAccessor.Value.HttpContext.UserId(),
                    IdentifierId = requestDto.CommentId,
                    IsLike = false,
                });
                if (reactionResult.OperationResult is not OperationResult.Succeeded)
                {
                    return new(OperationResult.Failed) { Errors = reactionResult.Errors };
                }

                var result = await UpdateSchoolCommentReactionsAsync(requestDto.CommentId);

                return result;
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, },] };
            }
        }

        public async Task<ResultData<long>> CreateSchoolCommentContributionAsync([NotNull] ManageSchoolCommentContributionRequestDto requestDto)
        {
            try
            {
                var commentSpecification = new SchoolIdEqualsSpecification<SchoolComment>(requestDto.SchoolId)
                        .And(new CreationUserIdEqualsSpecification<SchoolComment, ApplicationUser, int>(requestDto.CreationUserId));
                var commentExists = await CommentExistsAsync(commentSpecification);
                if (commentExists.Data)
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = "Comment Exists for Current User and School", }] };
                }

                var contributionSpecification = new CreationUserIdEqualsSpecification<Contribution, ApplicationUser, int>(requestDto.CreationUserId)
                    .And(new IdentifierIdEqualsSpecification<Contribution>(requestDto.SchoolId))
                    .And(new StatusEqualsSpecification<Contribution>(Status.Draft));
                var contributionExists = await contributionService.Value.ExistsContributionAsync(contributionSpecification);
                if (contributionExists.Data)
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = "there is a pending Comment", }] };
                }

                SchoolCommentContributionDto dto = new()
                {
                    SchoolId = requestDto.SchoolId,
                    Comment = requestDto.Comment,
                    CreationDate = requestDto.CreationDate,
                    CreationUserId = requestDto.CreationUserId,
                    ArtisticActivitiesRate = requestDto.ArtisticActivitiesRate,
                    BehaviorRate = requestDto.BehaviorRate,
                    ClassesQualityRate = requestDto.ClassesQualityRate,
                    EducationRate = requestDto.EducationRate,
                    FacilitiesRate = requestDto.FacilitiesRate,
                    ITTrainingRate = requestDto.ITTrainingRate,
                    SafetyAndHappinessRate = requestDto.SafetyAndHappinessRate,
                    TuitionRatioRate = requestDto.TuitionRatioRate,
                    AverageRate = CalculateAverageRate(requestDto),
                };

                var contributionResult = await contributionService.Value.ManageContributionAsync(new ManageContributionRequestDto
                {
                    CategoryType = CategoryType.SchoolComment,
                    IdentifierId = requestDto.SchoolId,
                    Status = Status.Draft,
                    Data = JsonSerializer.Serialize(dto),
                });
                if (contributionResult.OperationResult is not OperationResult.Succeeded)
                {
                    return new(contributionResult.OperationResult) { Errors = contributionResult.Errors };
                }

                var hasAutoConfirmSchoolComment = await identityService.Value.HasClaimAsync(requestDto.CreationUserId, SystemClaim.AutoConfirmSchoolComment);
                if (hasAutoConfirmSchoolComment.Data || configuration.Value.GetValue<bool>("AutoConfirmComments"))
                {
                    _ = await ConfirmSchoolCommentContributionAsync(new()
                    {
                        ContributionId = contributionResult.Data,
                    });
                }

                return new(OperationResult.Succeeded) { Data = contributionResult.Data };

                static double CalculateAverageRate(ManageSchoolCommentContributionRequestDto dto) => (
                        dto.ArtisticActivitiesRate +
                        dto.BehaviorRate +
                        dto.ClassesQualityRate +
                        dto.EducationRate +
                        dto.FacilitiesRate +
                        dto.ITTrainingRate +
                        dto.SafetyAndHappinessRate +
                        dto.TuitionRatioRate
                        ) / 8;
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        public async Task<ResultData<bool>> ConfirmSchoolCommentContributionAsync([NotNull] ConfirmSchoolCommentContributionRequestDto requestDto)
        {
            try
            {
                var contributionSpecification = new IdEqualsSpecification<Contribution, long>(requestDto.ContributionId)
                    .And(new CategoryTypeEqualsSpecification<Contribution>(CategoryType.SchoolComment));
                var result = await contributionService.Value.ConfirmContributionAsync(contributionSpecification);
                if (result.Data is null)
                {
                    return new(OperationResult.Failed) { Errors = result.Errors };
                }

                var dto = JsonSerializer.Deserialize<SchoolCommentContributionDto>(result.Data.Data!)!;
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var schoolImageRepository = uow.GetRepository<SchoolComment>();
                schoolImageRepository.Add(new()
                {
                    SchoolId = dto.SchoolId,
                    Comment = dto.Comment,
                    AverageRate = dto.AverageRate,
                    ArtisticActivitiesRate = dto.ArtisticActivitiesRate,
                    BehaviorRate = dto.BehaviorRate,
                    ClassesQualityRate = dto.ClassesQualityRate,
                    EducationRate = dto.EducationRate,
                    FacilitiesRate = dto.FacilitiesRate,
                    ITTrainingRate = dto.ITTrainingRate,
                    SafetyAndHappinessRate = dto.SafetyAndHappinessRate,
                    TuitionRatioRate = dto.TuitionRatioRate,
                    CreationUserId = dto.CreationUserId,
                    CreationDate = dto.CreationDate,
                });
                _ = await uow.SaveChangesAsync();

                //this is temporary
                _ = await UpdateSchoolScoreAsync(dto.SchoolId);

                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, },] };
            }
        }

        public async Task<ResultData<bool>> CommentExistsAsync([NotNull] ISpecification<SchoolComment> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var exists = await uow.GetRepository<SchoolComment>().AnyAsync(specification);

                return new(OperationResult.Succeeded) { Data = exists };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, },] };
            }
        }

        #endregion

        #region Images

        public async Task<ResultData<ListDataSource<SchoolImageDto>>> GetSchoolImagesAsync(ListRequestDto<SchoolImage>? requestDto = null)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var result = await uow.GetRepository<SchoolImage>().GetManyQueryable(requestDto?.Specification).FilterListAsync(requestDto?.PagingDto);
                var users = await result.List.Select(t => new SchoolImageDto
                {
                    Id = t.Id,
                    CreationUser = t.CreationUser!.FirstName + " " + t.CreationUser.LastName,
                    CreationUserAvatar = t.CreationUser!.Avatar,
                    CreationDate = t.CreationDate,
                    FileId = t.FileId,
                    FileType = t.FileType,
                    SchoolId = t.SchoolId,
                    SchoolName = t.School!.Name,
                    TagId = t.TagId,
                }).ToListAsync();
                return new(OperationResult.Succeeded) { Data = new() { List = users, TotalRecordsCount = result.TotalRecordsCount } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<IEnumerable<SchoolImageInfoDto>>> GetSchoolImagesListAsync([NotNull] ISpecification<SchoolImage> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var result = await uow.GetRepository<SchoolImage>().GetManyQueryable(specification)
                    .Select(t => new
                    {
                        t.Id,
                        t.FileId,
                        t.CreationUserId,
                        CreationUser = t.CreationUser.FirstName + " " + t.CreationUser.LastName,
                        TagName = t.Tag != null ? t.Tag.Name : null,
                        TagIcon = t.Tag != null ? t.Tag.Icon : null,
                        t.TagId,
                    }).ToListAsync();

                return new(OperationResult.Succeeded)
                {
                    Data = result.Select(t =>
                        new SchoolImageInfoDto
                        {
                            Id = t.Id,
                            FileUri = fileService.Value.GetFileUri(t.FileId!, ContainerType.School).Data,
                            CreationUserId = t.CreationUserId,
                            CreationUser = t.CreationUser,
                            TagName = t.TagName,
                            TagIcon = t.TagIcon,
                            TagId = t.TagId,
                        })
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<long>> CreateSchoolImageContributionAsync([NotNull] ManageSchoolImageContributionRequestDto requestDto)
        {
            try
            {
                if (requestDto.TagId.HasValue)
                {
                    var specification = new IdEqualsSpecification<Domain.Entity.Tag, long>(requestDto.TagId.Value)
                        .And(new TagTypeEqualsSpecification(TagType.School));
                    var exists = await tagService.Value.ExistsTagAsync(specification);
                    if (!exists.Data)
                    {
                        return new(OperationResult.Failed) { Errors = [new() { Message = "Tag not found", }] };
                    }
                }

                using MemoryStream stream = new();
                await requestDto.File.CopyToAsync(stream);

                var fileId = await fileService.Value.UploadFileAsync(new()
                {
                    File = stream.ToArray(),
                    ContainerType = ContainerType.School,
                    FileExtension = Path.GetExtension(requestDto.File.FileName),
                });
                if (fileId.OperationResult is not OperationResult.Succeeded)
                {
                    return new(fileId.OperationResult) { Errors = fileId.Errors, };
                }

                SchoolImageContributionDto dto = new()
                {
                    FileId = fileId.Data,
                    FileType = requestDto.FileType,
                    SchoolId = requestDto.SchoolId,
                    TagId = requestDto.TagId,
                };
                var contributionResult = await contributionService.Value.ManageContributionAsync(new ManageContributionRequestDto
                {
                    CategoryType = CategoryType.SchoolImage,
                    IdentifierId = requestDto.SchoolId,
                    Status = Status.Draft,
                    Data = JsonSerializer.Serialize(dto),
                });
                if (contributionResult.OperationResult is not OperationResult.Succeeded)
                {
                    return new(contributionResult.OperationResult) { Errors = contributionResult.Errors };
                }

                var hasAutoConfirmSchoolImage = await identityService.Value.HasClaimAsync(requestDto.CreationUserId, SystemClaim.AutoConfirmSchoolImage);
                if (hasAutoConfirmSchoolImage.Data || await IsImageLocationNearSchoolAsync())
                {
                    _ = await ConfirmSchoolImageContributionAsync(new()
                    {
                        ContributionId = contributionResult.Data,
                    });
                }

                return new(OperationResult.Succeeded) { Data = contributionResult.Data };

                async Task<bool> IsImageLocationNearSchoolAsync()
                {
                    try
                    {
                        var gps = ImageMetadataReader.ReadMetadata(stream)
                             .OfType<GpsDirectory>()
                             .FirstOrDefault()?.GetGeoLocation();
                        if (gps is not null)
                        {
                            var geometryFactory = NtsGeometryServices.Instance.CreateGeometryFactory(4326);
                            var point = geometryFactory.CreatePoint(new Coordinate(gps.Longitude, gps.Latitude));

                            var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                            var schoolRepository = uow.GetRepository<School, long>();
                            var schoolCoordinates = await schoolRepository.GetManyQueryable(t => t.Id == requestDto.SchoolId).Select(t => t.Coordinates).FirstOrDefaultAsync();
                            if (schoolCoordinates is not null && schoolCoordinates.Distance(point) < 200)
                            {
                                return true;
                            }
                        }
                    }
                    catch
                    {
                        //ignore
                    }

                    return false;
                }
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        public async Task<ResultData<bool>> ConfirmSchoolImageContributionAsync([NotNull] ConfirmSchoolImageContributionRequestDto requestDto)
        {
            try
            {
                var contributionSpecification = new IdEqualsSpecification<Contribution, long>(requestDto.ContributionId)
                    .And(new CategoryTypeEqualsSpecification<Contribution>(CategoryType.SchoolImage));
                var result = await contributionService.Value.ConfirmContributionAsync(contributionSpecification);
                if (result.Data is null)
                {
                    return new(OperationResult.Failed) { Errors = result.Errors };
                }


                var dto = JsonSerializer.Deserialize<SchoolImageContributionDto>(result.Data.Data!)!;
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var schoolImageRepository = uow.GetRepository<SchoolImage>();
                schoolImageRepository.Add(new()
                {
                    FileId = dto.FileId,
                    FileType = dto.FileType,
                    SchoolId = dto.SchoolId,
                    TagId = dto.TagId,
                    CreationUserId = result.Data.CreationUserId,
                    CreationDate = result.Data.CreationDate,
                });
                _ = await uow.SaveChangesAsync();

                var schoolRepository = uow.GetRepository<School>();
                _ = await schoolRepository.GetManyQueryable(t => t.Id == dto.SchoolId).ExecuteUpdateAsync(t => t
                    .SetProperty(p => p.LastModifyUserId, result.Data.CreationUserId)
                    .SetProperty(p => p.LastModifyDate, DateTimeOffset.UtcNow));

                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, },] };
            }
        }

        public async Task<ResultData<bool>> RemoveSchoolImageAsync([NotNull] ISpecification<SchoolImage> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<SchoolImage>();
                var schoolImage = await repository.GetAsync(specification);
                if (schoolImage is null)
                {
                    return new(OperationResult.NotFound) { Errors = [new() { Message = Localizer.Value["SchoolImageNotFound"] },], };
                }

                repository.Remove(schoolImage);
                _ = await uow.SaveChangesAsync();

                _ = await fileService.Value.RemoveFileAsync(new()
                {
                    FileId = schoolImage.FileId!,
                    ContainerType = ContainerType.School
                });

                _ = await contributionService.Value.DeleteContributionAsync(new IdEqualsSpecification<Contribution, long>(schoolImage.ContributionId.GetValueOrDefault()));

                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, },] };
            }
        }

        public async Task<ResultData<bool>> ManageSchoolImageAsync([NotNull] ManageSchoolImageRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<SchoolImage>();

                var specification = new IdEqualsSpecification<SchoolImage, long>(requestDto.Id)
                    .And(new SchoolIdEqualsSpecification<SchoolImage>(requestDto.SchoolId));
                var schoolImage = await repository.GetAsync(specification);
                if (schoolImage is null)
                {
                    return new(OperationResult.NotFound) { Errors = [new() { Message = Localizer.Value["SchoolImageNotFound"] },], };
                }

                if (requestDto.TagId.HasValue)
                {
                    var tagSpecification = new IdEqualsSpecification<Domain.Entity.Tag, long>(requestDto.TagId.Value)
                        .And(new TagTypeEqualsSpecification(TagType.School));
                    var exists = await tagService.Value.ExistsTagAsync(tagSpecification);
                    if (!exists.Data)
                    {
                        return new(OperationResult.NotFound) { Errors = [new() { Message = Localizer.Value["TagNotFound"] },], };
                    }
                }

                schoolImage.TagId = requestDto.TagId;

                _ = repository.Update(schoolImage);
                _ = await uow.SaveChangesAsync();

                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (ReferenceConstraintException)
            {
                return new(OperationResult.NotValid) { Errors = [new() { Message = Localizer.Value["InvalidTagId"], }] };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        #endregion

        #region Contributions

        public async Task<ResultData<long>> ManageSchoolContributionAsync([NotNull] ManageSchoolContributionRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var exists = await uow.GetRepository<School, long>().AnyAsync(t => t.Id == requestDto.SchoolId);
                if (!exists)
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = "School not found", },] };
                }

                if (requestDto.Id.HasValue)
                {
                    var specification = new IdEqualsSpecification<Contribution, long>(requestDto.Id.Value)
                        .And(new IdentifierIdEqualsSpecification<Contribution>(requestDto.SchoolId))
                        .And(new CreationUserIdEqualsSpecification<Contribution, ApplicationUser, int>(requestDto.UserId))
                        .And(new CategoryTypeEqualsSpecification<Contribution>(CategoryType.School))
                        .And(new StatusEqualsSpecification<Contribution>(Status.Draft).Or(new StatusEqualsSpecification<Contribution>(Status.Rejected)));
                    var data = await contributionService.Value.ExistsContributionAsync(specification);
                    if (!data.Data)
                    {
                        return new(data.OperationResult) { Errors = data.Errors };
                    }
                }

                var contributionResult = await contributionService.Value.ManageContributionAsync(new ManageContributionRequestDto
                {
                    CategoryType = CategoryType.School,
                    IdentifierId = requestDto.SchoolId,
                    Status = Status.Draft,
                    Data = JsonSerializer.Serialize(requestDto.Data),
                    Id = requestDto.Id,
                });
                if (contributionResult.OperationResult is not OperationResult.Succeeded)
                {
                    return new(contributionResult.OperationResult) { Errors = contributionResult.Errors };
                }

                var hasAutoConfirmSchoolContribution = await identityService.Value.HasClaimAsync(requestDto.UserId, SystemClaim.AutoConfirmSchoolContribution);
                if (hasAutoConfirmSchoolContribution.Data)
                {
                    _ = await ConfirmSchoolContributionAsync(new()
                    {
                        ContributionId = contributionResult.Data,
                        SchoolId = requestDto.SchoolId,
                        Data = requestDto.Data,
                    });
                }

                return new(OperationResult.Succeeded) { Data = contributionResult.Data };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, },] };
            }
        }

        public async Task<ResultData<bool>> ConfirmSchoolContributionAsync([NotNull] ConfirmSchoolContributionRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var school = await uow.GetRepository<School, long>().GetAsync(requestDto.SchoolId);
                if (school is null)
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = "School not found", },] };
                }

                var contributionSpecification = new IdEqualsSpecification<Contribution, long>(requestDto.ContributionId)
                    .And(new CategoryTypeEqualsSpecification<Contribution>(CategoryType.School))
                    .And(new IdentifierIdEqualsSpecification<Contribution>(requestDto.SchoolId));
                var contributionResult = await contributionService.Value.ConfirmContributionAsync(contributionSpecification);
                if (contributionResult.Data is null)
                {
                    return new(OperationResult.Failed) { Errors = contributionResult.Errors };
                }

                ManageSchoolRequestDto manageSchoolRequestDto = new()
                {
                    Address = requestDto.Data.Address,
                    CityId = requestDto.Data.CityId,
                    CountryId = requestDto.Data.CountryId,
                    Email = requestDto.Data.Email,
                    FaxNumber = requestDto.Data.FaxNumber,
                    LocalAddress = requestDto.Data.LocalAddress,
                    LocalName = requestDto.Data.LocalName,
                    Name = requestDto.Data.Name,
                    PhoneNumber = requestDto.Data.PhoneNumber,
                    Quarter = requestDto.Data.Quarter,
                    SchoolType = requestDto.Data.SchoolType,
                    StateId = requestDto.Data.StateId,
                    WebSite = requestDto.Data.WebSite,
                    ZipCode = requestDto.Data.ZipCode,
                    Id = requestDto.SchoolId,
                    Tags = requestDto.Data.Tags,
                    UserId = contributionResult.Data.CreationUserId,
                    Date = contributionResult.Data.CreationDate,
                };
                if (requestDto.Data.Latitude.HasValue && requestDto.Data.Longitude.HasValue)
                {
                    var geometryFactory = NtsGeometryServices.Instance.CreateGeometryFactory(4326);
                    manageSchoolRequestDto.Coordinates = geometryFactory.CreatePoint(new Coordinate(requestDto.Data.Longitude.Value, requestDto.Data.Latitude.Value));
                }
                var manageSchoolResult = await ManageSchoolAsync(manageSchoolRequestDto, true);

                return manageSchoolResult.OperationResult is OperationResult.Succeeded
                    ? new(OperationResult.Failed) { Errors = contributionResult.Errors }
                    : new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, },] };
            }
        }

        #endregion

        #region Issues

        public async Task<ResultData<long>> CreateSchoolIssuesContributionAsync([NotNull] CreateSchoolIssuesContributionRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<School>();
                var schoolExists = await repository.AnyAsync(t => t.Id == requestDto.SchoolId && !t.IsDeleted);
                if (!schoolExists)
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = "School not found", },] };
                }

                var contributionSpecification = new CreationUserIdEqualsSpecification<Contribution, ApplicationUser, int>(requestDto.CreationUserId)
                    .And(new IdentifierIdEqualsSpecification<Contribution>(requestDto.SchoolId))
                    .And(new StatusEqualsSpecification<Contribution>(Status.Draft));
                var contributionExists = await contributionService.Value.ExistsContributionAsync(contributionSpecification);
                if (contributionExists.Data)
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = "there is a pending issues", }] };
                }

                var contributionResult = await contributionService.Value.ManageContributionAsync(new()
                {
                    CategoryType = CategoryType.SchoolIssues,
                    IdentifierId = requestDto.SchoolId,
                    Status = Status.Draft,
                    Data = requestDto.Description,
                });
                if (contributionResult.OperationResult is not OperationResult.Succeeded)
                {
                    return new(contributionResult.OperationResult) { Errors = contributionResult.Errors };
                }

                var hasAutoConfirmSchoolContribution = await identityService.Value.HasClaimAsync(requestDto.CreationUserId, SystemClaim.AutoConfirmSchoolContribution);
                if (hasAutoConfirmSchoolContribution.Data)
                {
                    _ = await ConfirmSchoolIssuesContributionAsync(new()
                    {
                        ContributionId = contributionResult.Data,
                    });
                }

                return new(OperationResult.Succeeded) { Data = contributionResult.Data };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        public async Task<ResultData<bool>> ConfirmSchoolIssuesContributionAsync([NotNull] ConfirmSchoolIssuesContributionRequestDto requestDto)
        {
            try
            {
                var specification = new IdEqualsSpecification<Contribution, long>(requestDto.ContributionId)
                    .And(new CategoryTypeEqualsSpecification<Contribution>(CategoryType.SchoolIssues));
                var result = await contributionService.Value.ConfirmContributionAsync(specification);
                if (result.Data is null)
                {
                    return new(OperationResult.Failed) { Errors = result.Errors };
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<School>();
                var school = await repository.GetAsync(result.Data.IdentifierId.GetValueOrDefault());
                if (school is null)
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = "School not found", },] };
                }

                school.IsDeleted = true;
                _ = repository.Update(school);
                _ = await uow.SaveChangesAsync();

                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, },] };
            }
        }

        #endregion

        #region Job

        public async Task<ResultData<bool>> UpdateSchoolScoreAsync(long? schoolId = null)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var where = schoolId.HasValue ? $"WHERE s.Id={schoolId.Value}" : "";
                var query = $@"UPDATE s SET
                    Score=(SELECT AVG(c.AverageRate) FROM SchoolComments c WHERE c.SchoolId = s.Id)
                FROM Schools s {where}";
                _ = await uow.ExecuteSqlCommandAsync(query);

                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, },] };
            }
        }

        public async Task<ResultData<bool>> UpdateSchoolCommentReactionsAsync(long? schoolCommentId = null)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var where = schoolCommentId.HasValue ? $"WHERE c.Id={schoolCommentId.Value}" : "";
                var query = $@"UPDATE c SET
                    LikeCount=(SELECT COUNT(1) FROM Reactions r WHERE r.CategoryType={CategoryType.SchoolComment.Value} AND r.IdentifierId=c.Id AND r.IsLike=1)
                    ,DislikeCount=(SELECT COUNT(1) FROM Reactions r WHERE r.CategoryType={CategoryType.SchoolComment.Value} AND r.IdentifierId=c.Id AND r.IsLike=0)
                FROM SchoolComments c {where}";
                _ = await uow.ExecuteSqlCommandAsync(query);

                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, },] };
            }
        }

        public async Task<ResultData<bool>> RemoveOldRejectedSchoolImagesAsync()
        {
            try
            {
                var lst = await contributionService.Value.GetContributionsAsync(new()
                {
                    PagingDto = new() { PageFilter = new() { ReturnTotalRecordsCount = false, Size = 1000 } },
                    Specification = new CategoryTypeEqualsSpecification<Contribution>(CategoryType.SchoolImage)
                        .And(new StatusEqualsSpecification<Contribution>(Status.Rejected)),
                }, true);

                if (lst.Data.List is null)
                {
                    return new(OperationResult.Succeeded) { Data = true };
                }

                foreach (var item in lst.Data.List)
                {
                    var days = configuration.Value.GetValue<int>("DaysDistanceForRemoveOldRejectedSchoolImages") * -1;
                    if (!item.LastModifyDate.HasValue || item.LastModifyDate.Value > DateTimeOffset.UtcNow.AddDays(days))
                    {
                        continue;
                    }

                    var dto = JsonSerializer.Deserialize<SchoolImageContributionDto>(item.Data!);
                    if (dto is null)
                    {
                        continue;
                    }

                    var result = await fileService.Value.RemoveFileAsync(new()
                    {
                        FileId = dto.FileId!,
                        ContainerType = ContainerType.School,
                    });
                    if (result.Data)
                    {
                        _ = await contributionService.Value.ManageContributionAsync(new()
                        {
                            CategoryType = item.CategoryType!,
                            Id = item.Id,
                            Status = Status.Deleted,
                            IdentifierId = item.IdentifierId,
                            Data = item.Data,
                            Comment = item.Comment,
                        });
                    }
                }

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
