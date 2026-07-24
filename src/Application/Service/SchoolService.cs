namespace GamaEdtech.Application.Service
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;

    using EntityFramework.Exceptions.Common;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Core.Extensions.Linq;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Common.DataAccess.Specification.Impl;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.ApplicationSettings;
    using GamaEdtech.Data.Dto.Board;
    using GamaEdtech.Data.Dto.Contribution;
    using GamaEdtech.Data.Dto.School;
    using GamaEdtech.Data.Dto.SiteMap;
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

    public class SchoolService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor, Lazy<IStringLocalizer<SchoolService>> localizer, Lazy<IEmailService> emailService
        , Lazy<ILogger<SchoolService>> logger, Lazy<IFileService> fileService, Lazy<IContributionService> contributionService, Lazy<IIdentityService> identityService, Lazy<IApplicationSettingsService> applicationSettingsService
        , Lazy<IConfiguration> configuration, Lazy<ITagService> tagService, Lazy<IReactionService> reactionService, Lazy<ILocationService> locationService, Lazy<IBoardService> boardService, Lazy<IContentLocalizationService> contentLocalizationService)
        : LocalizableServiceBase<SchoolService>(unitOfWorkProvider, httpContextAccessor, localizer, logger), ISchoolService, ISiteMapHandler
    {
        #region SiteMap

        public ItemType ItemType => ItemType.School;

        public IQueryable<SiteMapItemDto> GetSiteMapData([NotNull] IUnitOfWork uow) => uow.GetRepository<School>().GetManyQueryable().Select(t => new SiteMapItemDto
        {
            Id = t.Id,
            Path1 = t.Id.ToString(),
            Path2 = t.Name,
            LastModifyDate = t.LastModifyDate ?? t.CreationDate,
        });

        #endregion

        #region Schools

        public async Task<ResultData<ListDataSource<SchoolsDto>>> GetSchoolsAsync(ListRequestDto<School>? requestDto = null)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var result = await uow.GetRepository<School>().GetManyQueryable(requestDto?.Specification).FilterListAsync(requestDto?.PagingDto);
                var schools = await result.List.Select(t => new
                {
                    t.Id,
                    t.Name,
                    t.LocalName,
                    t.DefaultImageId,
                    t.CountryRank,
                    t.StateRank,
                    t.CityRank,
                    Rating = t.CommentsRatingSum != null ? t.CommentsRatingSum / t.CommentsRatingCount : null,
                }).ToListAsync();
                if (schools is null || schools.Count == 0)
                {
                    return new(OperationResult.Succeeded) { Data = new() { List = null } };
                }

                var imageIds = schools.Where(t => t.DefaultImageId.HasValue).Select(t => t.DefaultImageId!.Value);
                var files = await uow.GetRepository<SchoolImage>().GetManyQueryable(t => imageIds.Contains(t.Id)).Select(t => new
                {
                    t.Id,
                    t.FileId,
                }).ToListAsync();

                List<SchoolsDto> lst = new(schools.Count);
                for (var i = 0; i < schools.Count; i++)
                {
                    lst.Add(new()
                    {
                        Id = schools[i].Id,
                        Name = schools[i].Name,
                        LocalName = schools[i].LocalName,
                        DefaultImageUri = fileService.Value.GetStaticFileUrl(new()
                        {
                            FileId = files.Find(c => c.Id == schools[i].DefaultImageId)?.FileId,
                            ContainerType = ContainerType.School,
                        }),
                        CountryRank = schools[i].CountryRank,
                        StateRank = schools[i].StateRank,
                        CityRank = schools[i].CityRank,
                        Rating = schools[i].Rating,
                    });
                }

                return new(OperationResult.Succeeded) { Data = new() { List = lst, TotalRecordsCount = result.TotalRecordsCount } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed)
                {
                    Errors = [new() { Message = exc.Message },]
                };
            }
        }

        public async Task<ResultData<ListDataSource<SchoolInfoDto>>> GetSchoolsListAsync(ListRequestDto<School>? requestDto = null, Point? point = null)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var lst = uow.GetRepository<School>().GetManyQueryable(requestDto?.Specification);
                int? total = requestDto?.PagingDto?.PageFilter?.ReturnTotalRecordsCount == true ? await lst.CountAsync() : null;
                var query = lst.Select(t => new
                {
                    t.Id,
                    t.Name,
                    LastModifyDate = t.LastModifyDate ?? t.CreationDate,
                    t.WebSite,
                    t.Email,
                    t.PhoneNumber,
                    t.Coordinates,
                    Rating = t.CommentsRatingSum != null ? t.CommentsRatingSum / t.CommentsRatingCount : null,
                    t.CityId,
                    t.StateId,
                    t.CountryId,
                    t.DefaultImageId,
                    t.CountryRank,
                    t.StateRank,
                    t.CityRank,
                    Distance = point != null && t.Coordinates != null ? t.Coordinates.Distance(point) : (double?)null,
                });

                (query, var sortApplied) = query.OrderBy(requestDto?.PagingDto?.SortFilter);
                if (!sortApplied)
                {
                    query = point is not null ? query.OrderBy(t => t.Distance) : query.OrderByDescending(t => t.Id);
                }
                if (requestDto?.PagingDto?.PageFilter is not null)
                {
                    query = query.Skip(requestDto.PagingDto.PageFilter.Skip)
                        .Take(requestDto.PagingDto.PageFilter.Size);
                }
                var items = await query.ToListAsync();
                if (items is null || items.Count == 0)
                {
                    return new(OperationResult.Succeeded) { Data = new() { List = null } };
                }

                HashSet<int> locationIds = [];
                List<long> imageIds = [];
                List<long> ids = new(items.Count);
                for (var i = 0; i < items.Count; i++)
                {
                    ids.Add(items[i].Id);

                    if (items[i].CountryId.HasValue)
                    {
                        _ = locationIds.Add(items[i].CountryId!.Value);
                    }

                    if (items[i].StateId.HasValue)
                    {
                        _ = locationIds.Add(items[i].StateId!.Value);
                    }

                    if (items[i].CityId.HasValue)
                    {
                        _ = locationIds.Add(items[i].CityId!.Value);
                    }

                    if (items[i].DefaultImageId.HasValue)
                    {
                        imageIds.Add(items[i].DefaultImageId!.Value);
                    }
                }
                var titles = await locationService.Value.GetTitlesAsync(new IdContainsSpecification<Domain.Entity.Location, int>(locationIds));
                var files = await uow.GetRepository<SchoolImage>().GetManyQueryable(t => imageIds.Contains(t.Id)).Select(t => new
                {
                    t.Id,
                    t.FileId,
                }).ToListAsync();

                var localizedValues = await contentLocalizationService.Value.GetLocalizedValuesAsync(new()
                {
                    ContentIds = ids,
                    ContentType = nameof(School),
                });

                List<SchoolInfoDto> result = new(items.Count);
                for (var i = 0; i < items.Count; i++)
                {
                    result.Add(new()
                    {
                        Id = items[i].Id,
                        Name = localizedValues.Data?.Find(t => t.ContentId == items[i].Id && t.Name == nameof(School.Name))?.Value ?? items[i].Name,
                        CityTitle = titles.Data?.Find(c => c.Key == items[i].CityId).Value,
                        Coordinates = items[i].Coordinates,
                        CountryTitle = titles.Data?.Find(c => c.Key == items[i].CountryId).Value,
                        StateTitle = titles.Data?.Find(c => c.Key == items[i].StateId).Value,
                        Distance = items[i].Distance,
                        LastModifyDate = items[i].LastModifyDate,
                        Rating = items[i].Rating,
                        HasEmail = !string.IsNullOrEmpty(items[i].Email),
                        HasPhoneNumber = !string.IsNullOrEmpty(items[i].PhoneNumber),
                        HasWebSite = !string.IsNullOrEmpty(items[i].WebSite),
                        DefaultImageUri = fileService.Value.GetStaticFileUrl(new()
                        {
                            FileId = files.Find(c => c.Id == items[i].DefaultImageId)?.FileId,
                            ContainerType = ContainerType.School,
                        }),
                        CountryRank = items[i].CountryRank,
                        StateRank = items[i].StateRank,
                        CityRank = items[i].CityRank,
                    });
                }

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
                var school = await uow.GetRepository<School>().GetManyQueryable(specification).Select(t => new
                {
                    t.Id,
                    t.Name,
                    t.LocalName,
                    t.Address,
                    t.LocalAddress,
                    t.Coordinates,
                    t.SchoolType,
                    t.ZipCode,
                    t.CityId,
                    CityTitle = t.City != null ? t.City.Title : "",
                    t.CountryId,
                    CountryTitle = t.Country != null ? t.Country.Title : "",
                    t.StateId,
                    StateTitle = t.State != null ? t.State.Title : "",
                    t.WebSite,
                    t.FaxNumber,
                    t.PhoneNumber,
                    t.Email,
                    t.Quarter,
                    t.OsmId,
                    t.Tuition,
                    t.Description,
                    DefaultImageId = t.DefaultImage != null ? t.DefaultImage.FileId : null,
                    t.ViewCount,
                    Tags = t.SchoolTags.Select(s => new TagDto
                    {
                        Icon = s.Tag.Icon,
                        Id = s.TagId,
                        Name = s.Tag.Name,
                        TagType = s.Tag.TagType,
                    }),
                    Boards = t.SchoolBoards.Select(s => new BoardDto
                    {
                        Id = s.BoardId,
                        Code = s.Board.Code,
                        Icon = s.Board.Icon,
                        Title = s.Board.Title,
                    }),
                    t.CountryRank,
                    t.StateRank,
                    t.CityRank,
                    Rating = t.CommentsRatingSum != null ? t.CommentsRatingSum / t.CommentsRatingCount : null,
                }).FirstOrDefaultAsync();
                if (school is null)
                {
                    return new(OperationResult.NotFound)
                    {
                        Errors = [new() { Message = Localizer.Value["SchoolNotFound"] },],
                    };
                }

                var localizedValues = await contentLocalizationService.Value.GetLocalizedValuesAsync(new()
                {
                    ContentIds = [school.Id],
                    ContentType = nameof(School),
                });

                SchoolDto result = new()
                {
                    Id = school.Id,
                    Name = localizedValues.Data?.Find(t => t.ContentId == school.Id && t.Name == nameof(School.Name))?.Value ?? school.Name,
                    LocalName = school.LocalName,
                    Address = localizedValues.Data?.Find(t => t.ContentId == school.Id && t.Name == nameof(School.Address))?.Value ?? school.Address,
                    LocalAddress = school.LocalAddress,
                    Coordinates = school.Coordinates,
                    SchoolType = school.SchoolType,
                    ZipCode = school.ZipCode,
                    CityId = school.CityId,
                    CityTitle = school.CityTitle,
                    CountryId = school.CountryId,
                    CountryTitle = school.CountryTitle,
                    StateId = school.StateId,
                    StateTitle = school.StateTitle,
                    WebSite = school.WebSite,
                    FaxNumber = school.FaxNumber,
                    PhoneNumber = school.PhoneNumber,
                    Email = school.Email,
                    Quarter = school.Quarter,
                    OsmId = school.OsmId,
                    Tuition = school.Tuition,
                    DefaultImageUri = fileService.Value.GetStaticFileUrl(new()
                    {
                        FileId = school.DefaultImageId,
                        ContainerType = ContainerType.School,
                    }),
                    Tags = school.Tags,
                    Boards = school.Boards,
                    Description = localizedValues.Data?.Find(t => t.ContentId == school.Id && t.Name == nameof(School.Description))?.Value ?? school.Description,
                    ViewCount = school.ViewCount,
                    CountryRank = school.CountryRank,
                    StateRank = school.StateRank,
                    CityRank = school.CityRank,
                    Rating = school.Rating,
                };
                return new(OperationResult.Succeeded) { Data = result };
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

        public async Task<ResultData<long>> ManageSchoolAsync([NotNull] ManageSchoolRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<School>();
                School? school = null;

                if (requestDto.Id.HasValue)
                {
                    school = await repository.GetAsync(requestDto.Id.Value, includes: (t) => t.Include(s => s.SchoolTags).Include(s => s.SchoolBoards));
                    if (school is null)
                    {
                        return new(OperationResult.NotFound)
                        {
                            Errors = [new() { Message = Localizer.Value["SchoolNotFound"] },],
                        };
                    }

                    school.Name = requestDto.Name ?? school.Name;
                    school.LocalName = requestDto.LocalName ?? school.LocalName;
                    school.Address = requestDto.Address ?? school.Address;
                    school.Coordinates = requestDto.Coordinates ?? school.Coordinates;
                    school.SchoolType = requestDto.SchoolType ?? school.SchoolType;
                    school.StateId = requestDto.StateId ?? school.StateId;
                    school.ZipCode = requestDto.ZipCode ?? school.ZipCode;
                    school.WebSite = requestDto.WebSite ?? school.WebSite;
                    school.Quarter = requestDto.Quarter ?? school.Quarter;
                    school.PhoneNumber = requestDto.PhoneNumber ?? school.PhoneNumber;
                    school.LocalAddress = requestDto.LocalAddress ?? school.LocalAddress;
                    school.FaxNumber = requestDto.FaxNumber ?? school.FaxNumber;
                    school.Email = requestDto.Email ?? school.Email;
                    school.CityId = requestDto.CityId ?? school.CityId;
                    school.CountryId = requestDto.CountryId ?? school.CountryId;
                    school.OsmId = requestDto.OsmId ?? school.OsmId;
                    school.Tuition = requestDto.Tuition ?? school.Tuition;
                    school.Description = requestDto.Description ?? school.Description;
                    school.LastModifyDate = requestDto.Date;
                    school.LastModifyUserId = requestDto.UserId;

                    _ = repository.Update(school);

                    if (requestDto.Tags?.Any() == true)
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

                    if (requestDto.Boards?.Any() == true)
                    {
                        var schoolBoardRepository = uow.GetRepository<SchoolBoard>();

                        var removedBoards = school.SchoolBoards?.Where(t => !requestDto.Boards.Contains(t.BoardId));
                        var newBoards = requestDto.Boards?.Where(t => school.SchoolBoards is null || school.SchoolBoards.All(s => s.BoardId != t));

                        if (removedBoards is not null)
                        {
                            foreach (var item in removedBoards)
                            {
                                schoolBoardRepository.Remove(item);
                            }
                        }

                        if (newBoards is not null)
                        {
                            foreach (var item in newBoards)
                            {
                                schoolBoardRepository.Add(new SchoolBoard
                                {
                                    SchoolId = requestDto.Id.Value,
                                    BoardId = item,
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
                        Tuition = requestDto.Tuition,
                        Description = requestDto.Description,
                    };
                    if (requestDto.Tags is not null)
                    {
                        school.SchoolTags = [.. requestDto.Tags.Select(t => new SchoolTag {
                            TagId = t,
                            CreationUserId=requestDto.UserId,
                            CreationDate=requestDto.Date,
                        })];
                    }
                    if (requestDto.Boards is not null)
                    {
                        var boards = await boardService.Value.GetBoardsListAsync();
                        if (boards.Data is null)
                        {
                            return new(OperationResult.NotFound)
                            {
                                Errors = [new() { Message = Localizer.Value["BoardsNotFound"] },],
                            };
                        }

                        school.SchoolBoards = [.. requestDto.Boards.Select(t => new SchoolBoard {
                            BoardId = t,
                            CreationUserId = requestDto.UserId,
                            CreationDate = requestDto.Date,
                        })];
                    }
                    repository.Add(school);
                }

                _ = await uow.SaveChangesAsync();

                if (requestDto.DefaultImageId.HasValue)
                {
                    var result = await SetDefaultSchoolImageAsync(new()
                    {
                        Id = requestDto.DefaultImageId.Value,
                        SchoolId = school.Id,
                    });
                    if (result.OperationResult is not OperationResult.Succeeded)
                    {
                        return new(result.OperationResult) { Errors = result.Errors };
                    }
                }

                return new(OperationResult.Succeeded) { Data = school.Id };
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
                var repository = uow.GetRepository<School>();
                var school = await repository.GetAsync(specification);
                if (school is null)
                {
                    return new(OperationResult.NotFound)
                    {
                        Data = false,
                        Errors = [new() { Message = Localizer.Value["SchoolNotFound"] },],
                    };
                }

                repository.Remove(school);
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
                var exists = await uow.GetRepository<School>().AnyAsync(specification);
                return new(OperationResult.Succeeded) { Data = exists };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task IncreaseSchoolViewAsync(long id)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                _ = await uow.GetRepository<School>().GetManyQueryable(t => t.Id == id).ExecuteUpdateAsync(t => t.SetProperty(p => p.ViewCount, p => p.ViewCount + 1));
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
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
                var query = await uow.GetRepository<SchoolComment>().GetManyQueryable(requestDto?.Specification).FilterListAsync(requestDto?.PagingDto);
                var lst = await query.List.Select(t => new
                {
                    t.Id,
                    t.Comment,
                    t.CreationUser.FirstName,
                    t.CreationUser.LastName,
                    t.CreationUser.AvatarId,
                    t.CreationDate,
                    t.LikeCount,
                    t.DislikeCount,
                    t.AverageRate,
                }).ToListAsync();
                if (lst is null)
                {
                    return new(OperationResult.Succeeded) { Data = new() { TotalRecordsCount = query.TotalRecordsCount } };
                }

                List<SchoolCommentDto> result = new(lst.Count);
                List<long> ids = new(lst.Count);
                for (var i = 0; i < lst.Count; i++)
                {
                    result.Add(new()
                    {
                        Id = lst[i].Id,
                        Comment = lst[i].Comment,
                        CreationUser = $"{lst[i].FirstName} {lst[i].LastName}",
                        CreationUserAvatarUri = fileService.Value.GetStaticFileUrl(new() { FileId = lst[i].AvatarId, ContainerType = ContainerType.User }),
                        CreationDate = lst[i].CreationDate,
                        LikeCount = lst[i].LikeCount,
                        DislikeCount = lst[i].DislikeCount,
                        AverageRate = lst[i].AverageRate,
                    });
                    ids.Add(lst[i].Id);
                }

                if (HttpContextAccessor.Value.HttpContext?.User.Identity?.IsAuthenticated == true)
                {
                    var spec = new CategoryTypeEqualsSpecification<Reaction>(CategoryType.SchoolComment)
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

                return new(OperationResult.Succeeded) { Data = new() { List = result, TotalRecordsCount = query.TotalRecordsCount } };
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
                        .And(new CreationUserIdEqualsSpecification<SchoolComment, ApplicationUser, long>(requestDto.UserId));
                var commentExists = await CommentExistsAsync(commentSpecification);
                if (commentExists.Data)
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = "Comment Exists for Current User and School", }] };
                }

                var contributionSpecification = new CreationUserIdEqualsSpecification<Contribution, ApplicationUser, long>(requestDto.UserId)
                    .And(new IdentifierIdEqualsSpecification<Contribution>(requestDto.SchoolId))
                    .And(new CategoryTypeEqualsSpecification<Contribution>(CategoryType.SchoolComment))
                    .And(
                        new StatusEqualsSpecification<Contribution>(Status.Draft)
                        .Or(new StatusEqualsSpecification<Contribution>(Status.Review))
                    );
                var contributionExists = await contributionService.Value.ExistsContributionAsync(contributionSpecification);
                if (contributionExists.Data)
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = "there is a pending Comment", }] };
                }

                var contributionResult = await contributionService.Value.ManageContributionAsync(new ManageContributionRequestDto<SchoolCommentContributionDto>
                {
                    CategoryType = CategoryType.SchoolComment,
                    IdentifierId = requestDto.SchoolId,
                    Status = Status.Review,
                    Data = requestDto.CommentContribution,
                });
                if (contributionResult.OperationResult is not OperationResult.Succeeded)
                {
                    return new(contributionResult.OperationResult) { Errors = contributionResult.Errors };
                }

                var hasAutoConfirmSchoolComment = await identityService.Value.HasClaimAsync(requestDto.UserId, SystemClaim.AutoConfirmSchoolComment);
                if (hasAutoConfirmSchoolComment.Data || configuration.Value.GetValue<bool>("AutoConfirmComments"))
                {
                    _ = await ConfirmSchoolCommentContributionAsync(new()
                    {
                        ContributionId = contributionResult.Data,
                        NotifyUser = false,
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

        private async Task CreateSchoolCommentAsync(SchoolCommentContributionDto dto)
        {
            var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
            var schoolCommentRepository = uow.GetRepository<SchoolComment>();
            var trn = uow.CreateTransactionScope();

            schoolCommentRepository.Add(new()
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

            _ = await uow.GetRepository<School>().GetManyQueryable(t => t.Id == dto.SchoolId).ExecuteUpdateAsync(t => t
                .SetProperty(p => p.CommentsRatingSum, p => p.CommentsRatingSum + dto.AverageRate)
                .SetProperty(p => p.CommentsRatingCount, p => p.CommentsRatingCount + 1));

            trn.Complete();
        }

        public async Task<ResultData<bool>> ConfirmSchoolCommentContributionAsync([NotNull] ConfirmSchoolCommentContributionRequestDto requestDto)
        {
            try
            {
                var contributionSpecification = new IdEqualsSpecification<Contribution, long>(requestDto.ContributionId)
                    .And(new CategoryTypeEqualsSpecification<Contribution>(CategoryType.SchoolComment));
                var result = await contributionService.Value.ConfirmContributionAsync<SchoolCommentContributionDto>(new()
                {
                    Specification = contributionSpecification,
                });
                if (result.Data is null)
                {
                    return new(OperationResult.Failed) { Errors = result.Errors };
                }

                await CreateSchoolCommentAsync(result.Data.Data!);

                if (requestDto.NotifyUser)
                {
                    var name = await GetSchoolsNameAsync(new IdEqualsSpecification<School, long>(result.Data.Data!.SchoolId));
                    var template = (await applicationSettingsService.Value.GetSettingAsync<string?>(nameof(ApplicationSettingsDto.SchoolCommentContributionConfirmationEmailTemplate))).Data;
                    template = template?
                        .Replace("[RECEIVER_NAME]", result.Data.FullName, StringComparison.OrdinalIgnoreCase)
                        .Replace("[SCHOOL_NAME]", name.Data?[0].Value, StringComparison.OrdinalIgnoreCase)
                        .Replace("[SCHOOL_ID]", result.Data.Data.SchoolId.ToString(), StringComparison.OrdinalIgnoreCase)
                        .Replace("[COMMENT]", result.Data.Data.Comment, StringComparison.OrdinalIgnoreCase);
                    _ = await emailService.Value.SendEmailAsync(new()
                    {
                        Subject = "School Comment Contribution Confirmation",
                        Body = template!,
                        EmailAddresses = [result.Data.Email],
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
                var query = await uow.GetRepository<SchoolImage>().GetManyQueryable(requestDto?.Specification).FilterListAsync(requestDto?.PagingDto);
                var lst = await query.List.Select(t => new
                {
                    t.Id,
                    t.CreationUser!.FirstName,
                    t.CreationUser.LastName,
                    t.CreationUser!.AvatarId,
                    t.CreationDate,
                    t.FileId,
                    t.FileType,
                    t.SchoolId,
                    SchoolName = t.School!.Name,
                    t.TagId,
                    t.IsDefault,
                }).ToListAsync();
                if (lst is null)
                {
                    return new(OperationResult.Succeeded) { Data = new() { TotalRecordsCount = query.TotalRecordsCount } };
                }


                List<SchoolImageDto> result = new(lst.Count);
                for (var i = 0; i < lst.Count; i++)
                {
                    result.Add(new()
                    {
                        Id = lst[i].Id,
                        CreationUser = $"{lst[i].FileId} {lst[i].LastName}",
                        CreationDate = lst[i].CreationDate,
                        FileType = lst[i].FileType,
                        SchoolId = lst[i].SchoolId,
                        SchoolName = lst[i].SchoolName,
                        TagId = lst[i].TagId,
                        IsDefault = lst[i].IsDefault,
                        CreationUserAvatarUri = fileService.Value.GetStaticFileUrl(new() { FileId = lst[i].AvatarId, ContainerType = ContainerType.User, }),
                        FileUri = fileService.Value.GetStaticFileUrl(new() { FileId = lst[i].FileId, ContainerType = ContainerType.School, }),
                    });
                }

                return new(OperationResult.Succeeded) { Data = new() { List = result, TotalRecordsCount = query.TotalRecordsCount } };
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
                var result = await uow.GetRepository<SchoolImage>().GetManyQueryable(specification).Select(t => new
                {
                    t.Id,
                    t.FileId,
                    t.CreationUserId,
                    CreationUser = t.CreationUser.FirstName + " " + t.CreationUser.LastName,
                    TagName = t.Tag != null ? t.Tag.Name : null,
                    TagIcon = t.Tag != null ? t.Tag.Icon : null,
                    t.TagId,
                    t.IsDefault,
                }).ToListAsync();

                List<SchoolImageInfoDto> lst = new(result.Count);
                for (var i = 0; i < result.Count; i++)
                {
                    lst.Add(new()
                    {
                        Id = result[i].Id,
                        FileUri = fileService.Value.GetStaticFileUrl(new()
                        {
                            FileId = result[i].FileId,
                            ContainerType = ContainerType.School,
                        }),
                        CreationUserId = result[i].CreationUserId,
                        CreationUser = result[i].CreationUser,
                        TagName = result[i].TagName,
                        TagIcon = result[i].TagIcon,
                        TagId = result[i].TagId,
                        IsDefault = result[i].IsDefault,
                    });
                }
                return new(OperationResult.Succeeded)
                {
                    Data = lst,
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

                var fileId = await fileService.Value.CreateFileAsync(new()
                {
                    File = requestDto.File,
                    ContainerType = ContainerType.School,
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
                    IsDefault = requestDto.IsDefault,
                };
                var contributionResult = await contributionService.Value.ManageContributionAsync(new ManageContributionRequestDto<SchoolImageContributionDto>
                {
                    CategoryType = CategoryType.SchoolImage,
                    IdentifierId = requestDto.SchoolId,
                    Status = Status.Review,
                    Data = dto,
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
                        NotifyUser = false,
                    });
                }

                return new(OperationResult.Succeeded) { Data = contributionResult.Data };

                async Task<bool> IsImageLocationNearSchoolAsync()
                {
                    try
                    {
                        GeoLocation gps = default;
                        using MemoryStream stream = new();
                        await requestDto.File.CopyToAsync(stream);
                        var exists = ImageMetadataReader.ReadMetadata(stream).OfType<GpsDirectory>().FirstOrDefault()?.TryGetGeoLocation(out gps);
                        if (exists == true)
                        {
                            var geometryFactory = NtsGeometryServices.Instance.CreateGeometryFactory(4326);
                            var point = geometryFactory.CreatePoint(new Coordinate(gps.Longitude, gps.Latitude));

                            var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                            var schoolRepository = uow.GetRepository<School>();
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
                var result = await contributionService.Value.ConfirmContributionAsync<SchoolImageContributionDto>(new()
                {
                    Specification = contributionSpecification,
                });
                if (result.Data is null)
                {
                    return new(OperationResult.Failed) { Errors = result.Errors };
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var schoolImageRepository = uow.GetRepository<SchoolImage>();
                var schoolImage = new SchoolImage
                {
                    FileId = result.Data.Data!.FileId,
                    FileType = result.Data.Data!.FileType,
                    SchoolId = result.Data.Data!.SchoolId,
                    TagId = result.Data.Data!.TagId,
                    IsDefault = result.Data.Data!.IsDefault,
                    CreationUserId = result.Data.CreationUserId,
                    CreationDate = result.Data.CreationDate,
                    ContributionId = requestDto.ContributionId,
                };
                schoolImageRepository.Add(schoolImage);
                _ = await uow.SaveChangesAsync();

                if (result.Data.Data!.IsDefault || (await schoolImageRepository.GetManyQueryable(t => t.SchoolId == schoolImage.SchoolId).Select(t => t.Id).Take(2).ToListAsync()).Count == 1)
                {
                    _ = await SetDefaultSchoolImageAsync(new()
                    {
                        Id = schoolImage.Id,
                        SchoolId = schoolImage.SchoolId,
                    });
                }

                await UpdateSchoolLastModifyDateAsync(uow, result.Data.CreationUserId, schoolImage.SchoolId);

                if (requestDto.NotifyUser)
                {
                    var name = await GetSchoolsNameAsync(new IdEqualsSpecification<School, long>(result.Data.Data!.SchoolId));
                    var template = (await applicationSettingsService.Value.GetSettingAsync<string?>(nameof(ApplicationSettingsDto.SchoolImageContributionConfirmationEmailTemplate))).Data;
                    template = template?
                        .Replace("[RECEIVER_NAME]", result.Data.FullName, StringComparison.OrdinalIgnoreCase)
                        .Replace("[SCHOOL_NAME]", name.Data?[0].Value, StringComparison.OrdinalIgnoreCase)
                        .Replace("[SCHOOL_ID]", result.Data.Data.SchoolId.ToString(), StringComparison.OrdinalIgnoreCase);
                    _ = await emailService.Value.SendEmailAsync(new()
                    {
                        Subject = "School Image Contribution Confirmation",
                        Body = template!,
                        EmailAddresses = [result.Data.Email],
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

        public async Task<ResultData<bool>> RejectSchoolImageContributionAsync([NotNull] RejectContributionRequestDto requestDto)
        {
            try
            {
                var contributionResult = await contributionService.Value.RejectContributionAsync<SchoolImageContributionDto>(requestDto);
                if (contributionResult.OperationResult is not OperationResult.Succeeded)
                {
                    return new(contributionResult.OperationResult) { Errors = contributionResult.Errors };
                }

                if (contributionResult.Data!.IdentifierId.HasValue)
                {
                    var name = await GetSchoolsNameAsync(new IdEqualsSpecification<School, long>(contributionResult.Data.IdentifierId.Value));
                    var template = (await applicationSettingsService.Value.GetSettingAsync<string?>(nameof(ApplicationSettingsDto.SchoolImageContributionRejectionEmailTemplate))).Data;
                    template = template?
                        .Replace("[RECEIVER_NAME]", contributionResult.Data.FullName, StringComparison.OrdinalIgnoreCase)
                        .Replace("[SCHOOL_NAME]", name.Data?[0].Value, StringComparison.OrdinalIgnoreCase)
                        .Replace("[REJECTION_REASON]", contributionResult.Data.Comment, StringComparison.OrdinalIgnoreCase)
                        .Replace("[SCHOOL_ID]", contributionResult.Data.IdentifierId.Value.ToString(), StringComparison.OrdinalIgnoreCase);
                    _ = await emailService.Value.SendEmailAsync(new()
                    {
                        Subject = "School Image Contribution Rejection",
                        Body = template!,
                        EmailAddresses = [contributionResult.Data.Email],
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

                if (schoolImage.IsDefault)
                {
                    _ = await uow.GetRepository<School>().GetManyQueryable(t => t.Id == schoolImage.SchoolId)
                        .ExecuteUpdateAsync(t => t.SetProperty(p => p.DefaultImageId, p => null));
                }

                repository.Remove(schoolImage);
                _ = await uow.SaveChangesAsync();

                if (schoolImage.IsDefault)
                {
                    var firstImageId = await repository.GetManyQueryable(t => t.SchoolId == schoolImage.SchoolId).Select(t => (long?)t.Id).FirstOrDefaultAsync();
                    if (firstImageId.HasValue)
                    {
                        _ = await repository.GetManyQueryable(t => t.Id == firstImageId.Value).ExecuteUpdateAsync(t => t.SetProperty(p => p.IsDefault, true));

                        _ = await uow.GetRepository<School>().GetManyQueryable(t => t.Id == schoolImage.SchoolId)
                            .ExecuteUpdateAsync(t => t.SetProperty(p => p.DefaultImageId, firstImageId.Value));
                    }
                }

                _ = await fileService.Value.RemoveFileAsync(new()
                {
                    FileId = schoolImage.FileId,
                    ContainerType = ContainerType.School,
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
                schoolImage.IsDefault = requestDto.IsDefault;

                _ = repository.Update(schoolImage);
                _ = await uow.SaveChangesAsync();

                if (requestDto.IsDefault)
                {
                    _ = await SetDefaultSchoolImageAsync(new()
                    {
                        Id = schoolImage.Id,
                        SchoolId = schoolImage.SchoolId,
                    });
                }

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

        public async Task<ResultData<bool>> SetDefaultSchoolImageAsync([NotNull] SetDefaultSchoolImageRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var affectedRows = await uow.GetRepository<SchoolImage>().GetManyQueryable(t => t.SchoolId == requestDto.SchoolId)
                    .ExecuteUpdateAsync(t => t.SetProperty(p => p.IsDefault, p => p.Id == requestDto.Id));

                _ = await uow.GetRepository<School>().GetManyQueryable(t => t.Id == requestDto.SchoolId)
                    .ExecuteUpdateAsync(t => t.SetProperty(p => p.DefaultImageId, requestDto.Id));

                return new(OperationResult.Succeeded) { Data = affectedRows > 0 };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        public async Task<ResultData<long>> CreateRemoveSchoolImageContributionAsync([NotNull] CreateRemoveSchoolImageContributionRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<SchoolImage>();
                var fileId = await repository.GetManyQueryable(t => t.Id == requestDto.ImageId && t.SchoolId == requestDto.SchoolId).Select(t => t.FileId).FirstOrDefaultAsync();
                if (string.IsNullOrEmpty(fileId))
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = "School Image not found", },] };
                }

                var contributionSpecification = new CreationUserIdEqualsSpecification<Contribution, ApplicationUser, long>(requestDto.CreationUserId)
                    .And(new IdentifierIdEqualsSpecification<Contribution>(requestDto.ImageId))
                    .And(new CategoryTypeEqualsSpecification<Contribution>(CategoryType.RemoveSchoolImage))
                    .And(
                        new StatusEqualsSpecification<Contribution>(Status.Draft)
                        .Or(new StatusEqualsSpecification<Contribution>(Status.Review))
                    );
                var contributionExists = await contributionService.Value.ExistsContributionAsync(contributionSpecification);
                if (contributionExists.Data)
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = "there is a pending remove image request", }] };
                }

                var contributionResult = await contributionService.Value.ManageContributionAsync<RemoveSchoolImageContributionDto>(new()
                {
                    CategoryType = CategoryType.RemoveSchoolImage,
                    IdentifierId = requestDto.ImageId,
                    Status = Status.Review,
                    Data = new()
                    {
                        SchoolId = requestDto.SchoolId,
                        FileId = fileId,
                        Description = requestDto.Description,
                    },
                });
                if (contributionResult.OperationResult is not OperationResult.Succeeded)
                {
                    return new(contributionResult.OperationResult) { Errors = contributionResult.Errors };
                }

                var hasAutoConfirmRemoveSchoolImage = await identityService.Value.HasClaimAsync(requestDto.CreationUserId, SystemClaim.AutoConfirmRemoveSchoolImage);
                if (hasAutoConfirmRemoveSchoolImage.Data)
                {
                    _ = await ConfirmRemoveSchoolImageContributionAsync(new()
                    {
                        ContributionId = contributionResult.Data,
                        NotifyUser = false,
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

        public async Task<ResultData<bool>> ConfirmRemoveSchoolImageContributionAsync([NotNull] ConfirmRemoveSchoolImageContributionRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                using var trn = uow.CreateTransactionScope();

                var contributionSpecification = new IdEqualsSpecification<Contribution, long>(requestDto.ContributionId)
                    .And(new CategoryTypeEqualsSpecification<Contribution>(CategoryType.RemoveSchoolImage));
                var result = await contributionService.Value.ConfirmContributionAsync<RemoveSchoolImageContributionDto?>(new()
                {
                    Specification = contributionSpecification,
                });
                if (result.Data is null)
                {
                    return new(OperationResult.Failed) { Errors = result.Errors };
                }

                var schoolImageRepository = uow.GetRepository<SchoolImage>();
                var schoolImage = await schoolImageRepository.GetAsync(result.Data.IdentifierId.GetValueOrDefault());
                if (schoolImage is null)
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = "School Image not found", },] };
                }

                if (schoolImage.IsDefault)
                {
                    var firstImageId = await schoolImageRepository.GetManyQueryable(t => t.SchoolId == schoolImage.SchoolId).Select(t => (long?)t.Id).FirstOrDefaultAsync();
                    if (firstImageId.HasValue)
                    {
                        _ = await SetDefaultSchoolImageAsync(new()
                        {
                            Id = schoolImage.Id,
                            SchoolId = schoolImage.SchoolId,
                        });
                    }
                    else
                    {
                        _ = await uow.GetRepository<School>().GetManyQueryable(t => t.Id == schoolImage.SchoolId).ExecuteUpdateAsync(t => t.SetProperty(p => p.DefaultImageId, (long?)null));
                    }
                }

                schoolImageRepository.Remove(schoolImage);
                _ = await uow.SaveChangesAsync();

                await UpdateSchoolLastModifyDateAsync(uow, result.Data.CreationUserId, schoolImage.SchoolId);

                if (requestDto.NotifyUser)
                {
                    var name = await GetSchoolsNameAsync(new IdEqualsSpecification<School, long>(schoolImage.SchoolId));
                    var template = (await applicationSettingsService.Value.GetSettingAsync<string?>(nameof(ApplicationSettingsDto.RemoveSchoolImageContributionConfirmationEmailTemplate))).Data;
                    template = template?
                        .Replace("[RECEIVER_NAME]", result.Data.FullName, StringComparison.OrdinalIgnoreCase)
                        .Replace("[SCHOOL_NAME]", name.Data?[0].Value, StringComparison.OrdinalIgnoreCase)
                        .Replace("[SCHOOL_ID]", schoolImage.SchoolId.ToString(), StringComparison.OrdinalIgnoreCase);
                    _ = await emailService.Value.SendEmailAsync(new()
                    {
                        Subject = "Remove School Image Contribution Confirmation",
                        Body = template!,
                        EmailAddresses = [result.Data.Email],
                    });
                }

                trn.Complete();
                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, },] };
            }
        }

        #endregion

        #region Contributions

        public async Task<ResultData<long>> ManageSchoolContributionAsync([NotNull] ManageSchoolContributionRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();

                if (requestDto.SchoolId.HasValue)
                {
                    var exists = await uow.GetRepository<School>().AnyAsync(t => t.Id == requestDto.SchoolId);
                    if (!exists)
                    {
                        return new(OperationResult.Failed) { Errors = [new() { Message = "School not found", },] };
                    }
                }

                if (requestDto.Id.HasValue)
                {
                    var specification = new IdEqualsSpecification<Contribution, long>(requestDto.Id.Value)
                        .And(new CreationUserIdEqualsSpecification<Contribution, ApplicationUser, long>(requestDto.UserId))
                        .And(new CategoryTypeEqualsSpecification<Contribution>(CategoryType.School))
                        .And(
                            new StatusEqualsSpecification<Contribution>(Status.Draft)
                            .Or(new StatusEqualsSpecification<Contribution>(Status.Rejected))
                            .Or(new StatusEqualsSpecification<Contribution>(Status.Review))
                        );
                    if (requestDto.SchoolId.HasValue)
                    {
                        specification = specification.And(new IdentifierIdEqualsSpecification<Contribution>(requestDto.SchoolId.Value));
                    }

                    var data = await contributionService.Value.ExistsContributionAsync(specification);
                    if (!data.Data)
                    {
                        return new(data.OperationResult) { Errors = data.Errors };
                    }
                }

                if (requestDto.File is not null)
                {
                    var fileId = await fileService.Value.CreateFileAsync(new()
                    {
                        File = requestDto.File,
                        ContainerType = ContainerType.School,
                    });
                    if (fileId.OperationResult is not OperationResult.Succeeded)
                    {
                        return new(fileId.OperationResult) { Errors = fileId.Errors, };
                    }

                    requestDto.SchoolContribution.ImageFileId = fileId.Data;
                    requestDto.SchoolContribution.IsDefault = requestDto.IsDefault;
                }

                var contributionResult = await contributionService.Value.ManageContributionAsync(new ManageContributionRequestDto<SchoolContributionDto>
                {
                    CategoryType = CategoryType.School,
                    IdentifierId = requestDto.SchoolId,
                    Status = Status.Review,
                    Data = requestDto.SchoolContribution,
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
                        NotifyUser = false,
                        SchoolId = requestDto.SchoolId,
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

                if (requestDto.SchoolId.HasValue)
                {
                    var schoolExists = await uow.GetRepository<School>().AnyAsync(t => t.Id == requestDto.SchoolId.Value);
                    if (!schoolExists)
                    {
                        return new(OperationResult.Failed) { Errors = [new() { Message = "School not found", },] };
                    }
                }

                var contributionSpecification = new IdEqualsSpecification<Contribution, long>(requestDto.ContributionId)
                    .And(new CategoryTypeEqualsSpecification<Contribution>(CategoryType.School));
                if (requestDto.SchoolId.HasValue)
                {
                    contributionSpecification = contributionSpecification.And(new IdentifierIdEqualsSpecification<Contribution>(requestDto.SchoolId.Value));
                }

                var contributionResult = await contributionService.Value.ConfirmContributionAsync<SchoolContributionDto>(new()
                {
                    Specification = contributionSpecification,
                });
                if (contributionResult.Data is null)
                {
                    return new(OperationResult.Failed) { Errors = contributionResult.Errors };
                }

                ManageSchoolRequestDto manageSchoolRequestDto = new()
                {
                    Address = contributionResult.Data.Data!.Address,
                    CityId = contributionResult.Data.Data.CityId,
                    CountryId = contributionResult.Data.Data.CountryId,
                    Email = contributionResult.Data.Data.Email,
                    FaxNumber = contributionResult.Data.Data.FaxNumber,
                    LocalAddress = contributionResult.Data.Data.LocalAddress,
                    LocalName = contributionResult.Data.Data.LocalName,
                    Name = contributionResult.Data.Data.Name,
                    PhoneNumber = contributionResult.Data.Data.PhoneNumber,
                    Quarter = contributionResult.Data.Data.Quarter,
                    SchoolType = contributionResult.Data.Data.SchoolType,
                    StateId = contributionResult.Data.Data.StateId,
                    WebSite = contributionResult.Data.Data.WebSite,
                    ZipCode = contributionResult.Data.Data.ZipCode,
                    Id = requestDto.SchoolId,
                    Tags = contributionResult.Data.Data.Tags,
                    Boards = contributionResult.Data.Data.Boards,
                    UserId = contributionResult.Data.CreationUserId,
                    Date = contributionResult.Data.CreationDate,
                    Tuition = contributionResult.Data.Data.Tuition,
                    DefaultImageId = contributionResult.Data.Data.DefaultImageId,
                    Description = contributionResult.Data.Data.Description,
                };
                if (contributionResult.Data.Data!.Latitude.HasValue && contributionResult.Data.Data!.Longitude.HasValue)
                {
                    var geometryFactory = NtsGeometryServices.Instance.CreateGeometryFactory(4326);
                    manageSchoolRequestDto.Coordinates = geometryFactory.CreatePoint(new Coordinate(contributionResult.Data.Data!.Longitude.Value, contributionResult.Data.Data!.Latitude.Value));
                }
                var manageSchoolResult = await ManageSchoolAsync(manageSchoolRequestDto);
                if (manageSchoolResult.OperationResult is not OperationResult.Succeeded)
                {
                    return new(OperationResult.Failed) { Errors = manageSchoolResult.Errors };
                }

                if (contributionResult.Data.Data!.DefaultImageId.HasValue)
                {
                    _ = await SetDefaultSchoolImageAsync(new()
                    {
                        Id = contributionResult.Data.Data.DefaultImageId.Value,
                        SchoolId = manageSchoolResult.Data,
                    });
                }

                if (!string.IsNullOrEmpty(contributionResult.Data.Data.ImageFileId))
                {
                    var schoolImageRepository = uow.GetRepository<SchoolImage>();
                    var schoolImage = new SchoolImage
                    {
                        FileId = contributionResult.Data.Data.ImageFileId,
                        FileType = ImageFileType.SimpleImage,
                        SchoolId = manageSchoolResult.Data,
                        CreationUserId = contributionResult.Data.CreationUserId,
                        CreationDate = contributionResult.Data.CreationDate,
                        ContributionId = requestDto.ContributionId,
                    };
                    schoolImageRepository.Add(schoolImage);
                    _ = await uow.SaveChangesAsync();

                    if (contributionResult.Data.Data.IsDefault || (await schoolImageRepository.GetManyQueryable(t => t.SchoolId == schoolImage.SchoolId).Select(t => t.Id).Take(2).ToListAsync()).Count == 1)
                    {
                        _ = await SetDefaultSchoolImageAsync(new()
                        {
                            Id = schoolImage.Id,
                            SchoolId = schoolImage.SchoolId,
                        });
                    }
                }

                if (contributionResult.Data.Data!.Comment is not null)
                {
                    contributionResult.Data.Data.Comment.SchoolId = manageSchoolResult.Data;
                    await CreateSchoolCommentAsync(contributionResult.Data.Data.Comment);
                }

                if (contributionResult.Data.Data.LocalizedValues is not null)
                {
                    foreach (var item in contributionResult.Data.Data.LocalizedValues)
                    {
                        _ = await contentLocalizationService.Value.ManageContentLocalizationAsync(new()
                        {
                            ContentId = requestDto.SchoolId.GetValueOrDefault(),
                            LanguageId = item.LanguageId,
                            ContentType = nameof(School),
                            Name = nameof(School.Address),
                            Value = item.Address,
                        });

                        _ = await contentLocalizationService.Value.ManageContentLocalizationAsync(new()
                        {
                            ContentId = requestDto.SchoolId.GetValueOrDefault(),
                            LanguageId = item.LanguageId,
                            ContentType = nameof(School),
                            Name = nameof(School.Description),
                            Value = item.Description,
                        });

                        _ = await contentLocalizationService.Value.ManageContentLocalizationAsync(new()
                        {
                            ContentId = requestDto.SchoolId.GetValueOrDefault(),
                            LanguageId = item.LanguageId,
                            ContentType = nameof(School),
                            Name = nameof(School.Name),
                            Value = item.Name,
                        });
                    }
                }

                if (requestDto.NotifyUser)
                {
                    var name = await GetSchoolsNameAsync(new IdEqualsSpecification<School, long>(manageSchoolResult.Data));
                    var template = (await applicationSettingsService.Value.GetSettingAsync<string?>(nameof(ApplicationSettingsDto.SchoolContributionConfirmationEmailTemplate))).Data;
                    template = template?
                        .Replace("[RECEIVER_NAME]", contributionResult.Data.FullName, StringComparison.OrdinalIgnoreCase)
                        .Replace("[SCHOOL_NAME]", name.Data?[0].Value, StringComparison.OrdinalIgnoreCase)
                        .Replace("[SCHOOL_ID]", manageSchoolResult.Data.ToString(), StringComparison.OrdinalIgnoreCase);
                    _ = await emailService.Value.SendEmailAsync(new()
                    {
                        Subject = "School Contribution Confirmation",
                        Body = template!,
                        EmailAddresses = [contributionResult.Data.Email],
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

        public async Task<ResultData<bool>> RejectSchoolContributionAsync([NotNull] RejectContributionRequestDto requestDto)
        {
            try
            {
                var contributionResult = await contributionService.Value.RejectContributionAsync<SchoolContributionDto>(requestDto);
                if (contributionResult.OperationResult is not OperationResult.Succeeded)
                {
                    return new(contributionResult.OperationResult) { Errors = contributionResult.Errors };
                }

                if (contributionResult.Data!.IdentifierId.HasValue)
                {
                    var name = await GetSchoolsNameAsync(new IdEqualsSpecification<School, long>(contributionResult.Data.IdentifierId.Value));
                    var template = (await applicationSettingsService.Value.GetSettingAsync<string?>(nameof(ApplicationSettingsDto.SchoolContributionRejectionEmailTemplate))).Data;
                    template = template?
                        .Replace("[RECEIVER_NAME]", contributionResult.Data.FullName, StringComparison.OrdinalIgnoreCase)
                        .Replace("[SCHOOL_NAME]", name.Data?[0].Value, StringComparison.OrdinalIgnoreCase)
                        .Replace("[REJECTION_REASON]", contributionResult.Data.Comment, StringComparison.OrdinalIgnoreCase)
                        .Replace("[SCHOOL_ID]", contributionResult.Data.IdentifierId.Value.ToString(), StringComparison.OrdinalIgnoreCase);
                    _ = await emailService.Value.SendEmailAsync(new()
                    {
                        Subject = "School Contribution Rejection",
                        Body = template!,
                        EmailAddresses = [contributionResult.Data.Email],
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

                var contributionSpecification = new CreationUserIdEqualsSpecification<Contribution, ApplicationUser, long>(requestDto.CreationUserId)
                    .And(new IdentifierIdEqualsSpecification<Contribution>(requestDto.SchoolId))
                    .And(
                        new StatusEqualsSpecification<Contribution>(Status.Draft)
                        .Or(new StatusEqualsSpecification<Contribution>(Status.Review))
                    );
                var contributionExists = await contributionService.Value.ExistsContributionAsync(contributionSpecification);
                if (contributionExists.Data)
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = "there is a pending issues", }] };
                }

                var contributionResult = await contributionService.Value.ManageContributionAsync<string>(new()
                {
                    CategoryType = CategoryType.SchoolIssues,
                    IdentifierId = requestDto.SchoolId,
                    Status = Status.Review,
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
                        NotifyUser = false,
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
                var result = await contributionService.Value.ConfirmContributionAsync<string>(new()
                {
                    Specification = specification,
                });
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

                if (requestDto.NotifyUser)
                {
                    var template = (await applicationSettingsService.Value.GetSettingAsync<string?>(nameof(ApplicationSettingsDto.SchoolIssuesContributionConfirmationEmailTemplate))).Data;
                    template = template?
                        .Replace("[RECEIVER_NAME]", result.Data.FullName, StringComparison.OrdinalIgnoreCase)
                        .Replace("[SCHOOL_NAME]", school.Name, StringComparison.OrdinalIgnoreCase)
                        .Replace("[SCHOOL_ID]", school.Id.ToString(), StringComparison.OrdinalIgnoreCase)
                        .Replace("[ISSUES]", school.Name, StringComparison.OrdinalIgnoreCase);
                    _ = await emailService.Value.SendEmailAsync(new()
                    {
                        Subject = "School Issue Contribution Confirmation",
                        Body = template!,
                        EmailAddresses = [result.Data.Email],
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

        #endregion

        #region Job

        public async Task<ResultData<bool>> UpdateSchoolScoreAsync()
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var query = $@"
;WITH ImageAgg AS
(
    SELECT i.SchoolId, CASE WHEN COUNT_BIG(*) >= 5 THEN 50 ELSE COUNT_BIG(*) * 10 END AS ImageScore FROM SchoolImages i GROUP BY i.SchoolId
),
ScoreCalc AS
(
    SELECT
        s.Id,
        s.CountryId,
        s.StateId,
        s.CityId,
        Score = ISNULL(ia.ImageScore, 0)
            + CASE WHEN s.CommentsRatingSum IS NOT NULL THEN s.CommentsRatingSum/s.CommentsRatingCount ELSE 0 END
            + CASE WHEN s.Coordinates IS NOT NULL THEN 10 ELSE 0 END
            + CASE WHEN s.WebSite     IS NOT NULL AND s.WebSite    <> '' THEN 25 ELSE 0 END
            + CASE WHEN s.Email       IS NOT NULL AND s.Email      <> '' THEN 5  ELSE 0 END
            + CASE WHEN s.PhoneNumber IS NOT NULL AND s.PhoneNumber <> '' THEN 5  ELSE 0 END
            + CASE WHEN s.Address     IS NOT NULL AND s.Address    <> '' THEN 5  ELSE 0 END
    FROM Schools s
    LEFT JOIN ImageAgg ia ON ia.SchoolId = s.Id
),
RankCalc AS
(
    SELECT
        sc.*,
		CASE
            WHEN sc.CountryId IS NULL THEN NULL
            ELSE DENSE_RANK() OVER (PARTITION BY sc.CountryId ORDER BY sc.Score DESC)
        END AS CountryRank,
		CASE
            WHEN sc.StateId IS NULL THEN NULL
            ELSE DENSE_RANK() OVER (PARTITION BY sc.CountryId, sc.StateId ORDER BY sc.Score DESC)
        END AS StateRank,
		CASE
            WHEN sc.CityId IS NULL THEN NULL
            ELSE DENSE_RANK() OVER (PARTITION BY sc.CountryId, sc.StateId, sc.CityId ORDER BY sc.Score DESC)
        END AS CityRank
    FROM ScoreCalc sc
)
UPDATE s
SET
    s.CountryRank = rc.CountryRank,
    s.StateRank = rc.StateRank,
    s.CityRank = rc.CityRank
FROM Schools s
JOIN RankCalc rc ON rc.Id = s.Id
WHERE
      s.CountryRank <> rc.CountryRank
   OR s.CountryRank IS NULL
   OR s.StateRank <> rc.StateRank
   OR s.StateRank IS NULL
   OR s.CityRank <> rc.CityRank
   OR s.CityRank IS NULL;
";
                _ = await uow.ExecuteSqlCommandAsync(query);

                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, },] };
            }
        }

        public async Task<ResultData<bool>> UpdateSchoolCommentsRatingAsync()
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var query = $@"
;WITH CommentAgg AS
(
    SELECT c.SchoolId, SUM(CONVERT(decimal(10,2), c.AverageRate)) AS RateSum , COUNT(1) AS RateCount
	FROM SchoolComments c GROUP BY c.SchoolId
),
RankCal AS
(
    SELECT
        s.Id,
        ca.RateSum,
		ca.RateCount
    FROM Schools s
    LEFT JOIN CommentAgg ca ON ca.SchoolId = s.Id
)
UPDATE s
SET
    s.CommentsRatingSum = rc.RateSum,
	s.CommentsRatingCount = rc.RateCount
FROM Schools s
JOIN RankCal rc ON rc.Id = s.Id
";
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
                var where = schoolCommentId.HasValue ? $" c.Id={schoolCommentId.Value} AND " : "";
                var query = $@"
;WITH ReactionAgg AS
(
    SELECT r.IdentifierId, SUM(CASE WHEN r.IsLike = 1 THEN 1 ELSE 0 END) AS LikeCount, SUM(CASE WHEN r.IsLike = 0 THEN 1 ELSE 0 END) AS DislikeCount
    FROM Reactions r WHERE r.CategoryType = {CategoryType.SchoolComment.Value} GROUP BY r.IdentifierId
)
UPDATE c
SET
    c.LikeCount    = ISNULL(ra.LikeCount, 0),
    c.DislikeCount = ISNULL(ra.DislikeCount, 0)
FROM SchoolComments c
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

        public async Task<ResultData<bool>> RemoveOldRejectedSchoolImagesAsync()
        {
            try
            {
                var lst = await contributionService.Value.GetContributionsAsync<SchoolImageContributionDto>(new()
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

                    if (item.Data is null)
                    {
                        continue;
                    }

                    var result = await fileService.Value.RemoveFileAsync(new()
                    {
                        FileId = item.Data.FileId,
                        ContainerType = ContainerType.School,
                    });
                    if (result.Data)
                    {
                        _ = await contributionService.Value.ManageContributionAsync<SchoolImageContributionDto>(new()
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

        #region Private methods

        private static async Task UpdateSchoolLastModifyDateAsync(IUnitOfWork uow, long userId, long schoolId) => _ = await uow.GetRepository<School>().GetManyQueryable(t => t.Id == schoolId).ExecuteUpdateAsync(t => t
            .SetProperty(p => p.LastModifyUserId, userId)
            .SetProperty(p => p.LastModifyDate, DateTimeOffset.UtcNow));

        #endregion
    }
}
