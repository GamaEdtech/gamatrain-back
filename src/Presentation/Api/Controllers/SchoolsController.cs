namespace GamaEdtech.Presentation.Api.Controllers
{
    using System.Diagnostics.CodeAnalysis;
    using System.Text.Json;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Common.DataAccess.Specification.Impl;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Data.Dto.School;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Domain.Specification;
    using GamaEdtech.Domain.Specification.School;
    using GamaEdtech.Presentation.ViewModel.School;
    using GamaEdtech.Presentation.ViewModel.Tag;

    using Microsoft.AspNetCore.Mvc;

    using NetTopologySuite.Geometries;

    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    public class SchoolsController(Lazy<ILogger<SchoolsController>> logger, Lazy<ISchoolService> schoolService, Lazy<IContributionService> contributionService)
        : ApiControllerBase<SchoolsController>(logger)
    {
        [HttpGet, Produces<ApiResponse<ListDataSource<SchoolInfoResponseViewModel>>>()]
        public async Task<IActionResult<ListDataSource<SchoolInfoResponseViewModel>>> GetSchools([NotNull, FromQuery] SchoolInfoRequestViewModel request)
        {
            try
            {
                ISpecification<School>? baseSpecification = null;
                if (request.CountryId.HasValue)
                {
                    baseSpecification = new CountryIdEqualsSpecification(request.CountryId.Value);
                }
                if (request.StateId.HasValue)
                {
                    var specification = new StateIdEqualsSpecification(request.StateId.Value);
                    baseSpecification = baseSpecification is null ? specification : baseSpecification.And(specification);
                }
                if (request.CityId.HasValue)
                {
                    var specification = new CityIdEqualsSpecification(request.CityId.Value);
                    baseSpecification = baseSpecification is null ? specification : baseSpecification.And(specification);
                }
                if (!string.IsNullOrEmpty(request.Name))
                {
                    var specification = new NameContainsSpecification(request.Name);
                    baseSpecification = baseSpecification is null ? specification : baseSpecification.And(specification);
                }
                if (request.HasScore.HasValue)
                {
                    var specification = new HasScoreEqualsSpecification(request.HasScore.Value);
                    baseSpecification = baseSpecification is null ? specification : baseSpecification.And(specification);
                }

                Point? point = null;
                if (request.Location is not null)
                {
                    var specification = new LocationIncludeSpecification(request.Location.Latitude!.Value, request.Location.Longitude!.Value, request.Location.Radius!.Value);
                    point = specification.Point;
                    baseSpecification = baseSpecification is null ? specification : baseSpecification.And(specification);
                }

                request.PagingDto ??= new();
                var result = await schoolService.Value.GetSchoolsListAsync(new ListRequestDto<School>
                {
                    PagingDto = request.PagingDto,
                    Specification = baseSpecification,
                }, point);
                return OkWithFilter<ListDataSource<SchoolInfoResponseViewModel>>(new(result.Errors)
                {
                    Data = result.Data.List is null ? new() : new()
                    {
                        List = result.Data.List.Select(t => new SchoolInfoResponseViewModel
                        {
                            Id = t.Id,
                            Name = t.Name,
                            Slug = t.Name.Slugify(),
                            LastModifyDate = t.LastModifyDate,
                            Score = t.Score,
                            CityTitle = t.CityTitle,
                            CountryTitle = t.CountryTitle,
                            HasEmail = t.HasEmail,
                            HasPhone = t.HasPhoneNumber,
                            HasWebsite = t.HasWebSite,
                            HasLocation = t.Coordinates is not null,
                            Latitude = t.Coordinates?.Y,
                            Longitude = t.Coordinates?.X,
                            StateTitle = t.StateTitle,
                        }),
                        TotalRecordsCount = result.Data.TotalRecordsCount,
                    },
                    Filters = [
                        new(JsonNamingPolicy.KebabCaseLower.ConvertName(nameof(SchoolInfoResponseViewModel.CityTitle)), request.CityId.HasValue ? result.Data.List?.FirstOrDefault()?.CityTitle : null),
                        new(JsonNamingPolicy.KebabCaseLower.ConvertName(nameof(SchoolInfoResponseViewModel.CountryTitle)), request.CountryId.HasValue ? result.Data.List?.FirstOrDefault()?.CountryTitle: null),
                        new(JsonNamingPolicy.KebabCaseLower.ConvertName(nameof(SchoolInfoResponseViewModel.StateTitle)), request.StateId.HasValue ? result.Data.List?.FirstOrDefault()?.StateTitle: null),
                    ]
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ListDataSource<SchoolInfoResponseViewModel>>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpGet("{id:long}"), Produces<ApiResponse<SchoolResponseViewModel>>()]
        public async Task<IActionResult<SchoolResponseViewModel>> GetSchool([FromRoute] long id)
        {
            try
            {
                var result = await schoolService.Value.GetSchoolAsync(new IdEqualsSpecification<School, long>(id));

                return Ok<SchoolResponseViewModel>(new(result.Errors)
                {
                    Data = result.Data is null ? null : new()
                    {
                        Id = result.Data.Id,
                        Address = result.Data.Address,
                        LocalAddress = result.Data.LocalAddress,
                        Name = result.Data.Name,
                        LocalName = result.Data.LocalName,
                        SchoolType = result.Data.SchoolType,
                        StateId = result.Data.StateId,
                        StateTitle = result.Data.StateTitle,
                        ZipCode = result.Data.ZipCode,
                        Latitude = result.Data.Coordinates?.Y,
                        Longitude = result.Data.Coordinates?.X,
                        WebSite = result.Data.WebSite,
                        Email = result.Data.Email,
                        CityId = result.Data.CityId,
                        CityTitle = result.Data.CityTitle,
                        CountryId = result.Data.CountryId,
                        CountryTitle = result.Data.CountryTitle,
                        FaxNumber = result.Data.FaxNumber,
                        PhoneNumber = result.Data.PhoneNumber,
                        Quarter = result.Data.Quarter,
                        OsmId = result.Data.OsmId,
                        Tags = result.Data.Tags?.Select(t => new TagResponseViewModel
                        {
                            Id = t.Id,
                            Icon = t.Icon,
                            Name = t.Name,
                            TagType = t.TagType,
                        }),
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<SchoolResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        #region Comments

        [HttpGet("{schoolId:long}/rate"), Produces<ApiResponse<SchoolRateResponseViewModel>>()]
        public async Task<IActionResult<SchoolRateResponseViewModel>> GetSchoolRate([FromRoute] long schoolId)
        {
            try
            {
                var result = await schoolService.Value.GetSchoolRateAsync(new SchoolIdEqualsSpecification<SchoolComment>(schoolId));

                return Ok<SchoolRateResponseViewModel>(new(result.Errors)
                {
                    Data = result.Data is null ? new() : new()
                    {
                        ArtisticActivitiesRate = result.Data.ArtisticActivitiesRate,
                        AverageRate = result.Data.AverageRate,
                        BehaviorRate = result.Data.BehaviorRate,
                        ClassesQualityRate = result.Data.ClassesQualityRate,
                        EducationRate = result.Data.EducationRate,
                        FacilitiesRate = result.Data.FacilitiesRate,
                        ITTrainingRate = result.Data.ITTrainingRate,
                        SafetyAndHappinessRate = result.Data.SafetyAndHappinessRate,
                        TuitionRatioRate = result.Data.TuitionRatioRate,
                        TotalCount = result.Data.TotalCount,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<SchoolRateResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpGet("{schoolId:long}/comments"), Produces<ApiResponse<ListDataSource<SchoolCommentsResponseViewModel>>>()]
        public async Task<IActionResult<ListDataSource<SchoolCommentsResponseViewModel>>> GetSchoolComments([FromRoute] long schoolId, [NotNull, FromQuery] SchoolCommentsRequestViewModel request)
        {
            try
            {
                var result = await schoolService.Value.GetSchoolCommentsAsync(new ListRequestDto<SchoolComment>
                {
                    PagingDto = request.PagingDto,
                    Specification = new SchoolIdEqualsSpecification<SchoolComment>(schoolId),
                });
                return Ok<ListDataSource<SchoolCommentsResponseViewModel>>(new(result.Errors)
                {
                    Data = result.Data.List is null ? new() : new()
                    {
                        List = result.Data.List.Select(t => new SchoolCommentsResponseViewModel
                        {
                            Id = t.Id,
                            AverageRate = t.AverageRate,
                            Comment = t.Comment,
                            CreationDate = t.CreationDate,
                            CreationUser = t.CreationUser,
                            CreationUserAvatar = t.CreationUserAvatar,
                            DislikeCount = t.DislikeCount,
                            LikeCount = t.LikeCount,
                        }),
                        TotalRecordsCount = result.Data.TotalRecordsCount,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ListDataSource<SchoolCommentsResponseViewModel>>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpPost("{schoolId:long}/comments"), Produces<ApiResponse<ManageSchoolCommentResponseViewModel>>()]
        [Permission(policy: null)]
        public async Task<IActionResult<ManageSchoolCommentResponseViewModel>> CreateSchoolComment([FromRoute] long schoolId, [NotNull] ManageSchoolCommentRequestViewModel request)
        {
            try
            {
                var result = await schoolService.Value.CreateSchoolCommentContributionAsync(new ManageSchoolCommentContributionRequestDto
                {
                    ArtisticActivitiesRate = request.ArtisticActivitiesRate,
                    BehaviorRate = request.BehaviorRate,
                    ClassesQualityRate = request.ClassesQualityRate,
                    Comment = request.Comment,
                    EducationRate = request.EducationRate,
                    FacilitiesRate = request.FacilitiesRate,
                    ITTrainingRate = request.ITTrainingRate,
                    SafetyAndHappinessRate = request.SafetyAndHappinessRate,
                    SchoolId = schoolId,
                    TuitionRatioRate = request.TuitionRatioRate,
                    CreationDate = DateTimeOffset.UtcNow,
                    CreationUserId = User.UserId(),
                });
                return Ok<ManageSchoolCommentResponseViewModel>(new(result.Errors)
                {
                    Data = new() { Id = result.Data, },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ManageSchoolCommentResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpPatch("{schoolId:long}/comments/{commentId:long}/like"), Produces<ApiResponse<bool>>()]
        [Permission(policy: null)]
        public async Task<IActionResult<bool>> LikeSchoolComment([FromRoute] long schoolId, [FromRoute] long commentId)
        {
            try
            {
                var result = await schoolService.Value.LikeSchoolCommentAsync(new()
                {
                    CommentId = commentId,
                    SchoolId = schoolId,
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

        [HttpPatch("{schoolId:long}/comments/{commentId:long}/dislike"), Produces<ApiResponse<bool>>()]
        [Permission(policy: null)]
        public async Task<IActionResult<bool>> DislikeSchoolComment([FromRoute] long schoolId, [FromRoute] long commentId)
        {
            try
            {
                var result = await schoolService.Value.DislikeSchoolCommentAsync(new()
                {
                    CommentId = commentId,
                    SchoolId = schoolId,
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

        #region Images

        [HttpGet("{schoolId:long}/images/{fileType:FileType}"), Produces<ApiResponse<IEnumerable<SchoolImageInfoViewModel>>>()]
        public async Task<IActionResult<IEnumerable<SchoolImageInfoViewModel>>> GetSchoolImagesList([FromRoute] long schoolId, [FromRoute] FileType fileType)
        {
            try
            {
                var specification = new SchoolIdEqualsSpecification<SchoolImage>(schoolId)
                    .And(new SchoolImageFileTypeEqualsSpecification(fileType));
                var result = await schoolService.Value.GetSchoolImagesListAsync(specification);
                return Ok<IEnumerable<SchoolImageInfoViewModel>>(new(result.Errors)
                {
                    Data = result.Data is null ? [] : result.Data.Select(t => new SchoolImageInfoViewModel
                    {
                        Id = t.Id,
                        CreationUser = t.CreationUser,
                        CreationUserId = t.CreationUserId,
                        FileUri = t.FileUri,
                        TagIcon = t.TagIcon,
                        TagId = t.TagId,
                        TagName = t.TagName,
                    }),
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<IEnumerable<SchoolImageInfoViewModel>>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpPost("{schoolId:long}/images/{fileType:FileType}"), Produces<ApiResponse<CreateSchoolImageResponseViewModel>>()]
        [Permission(policy: null)]
        public async Task<IActionResult<CreateSchoolImageResponseViewModel>> CreateSchoolImageContribution([FromRoute] long schoolId, [FromRoute] FileType fileType, [NotNull, FromForm] CreateSchoolImageRequestViewModel request)
        {
            try
            {
                var result = await schoolService.Value.CreateSchoolImageContributionAsync(new ManageSchoolImageContributionRequestDto
                {
                    File = request.File!,
                    FileType = fileType,
                    SchoolId = schoolId,
                    TagId = request.TagId,
                    CreationDate = DateTimeOffset.UtcNow,
                    CreationUserId = User.UserId(),
                });
                return Ok<CreateSchoolImageResponseViewModel>(new(result.Errors)
                {
                    Data = new() { Id = result.Data, },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<CreateSchoolImageResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        #endregion

        #region Contributions

        [HttpGet("{schoolId:long}/contributions"), Produces<ApiResponse<ListDataSource<SchoolContributionInfoListResponseViewModel>>>()]
        [Permission(policy: null)]
        public async Task<IActionResult<ListDataSource<SchoolContributionInfoListResponseViewModel>>> GetSchoolContributionList([FromRoute] long schoolId, [NotNull, FromQuery] SchoolContributionListRequestViewModel request)
        {
            try
            {
                var result = await contributionService.Value.GetContributionsAsync(new ListRequestDto<Contribution>
                {
                    PagingDto = request.PagingDto,
                    Specification = new IdentifierIdEqualsSpecification<Contribution>(schoolId)
                        .And(new CreationUserIdEqualsSpecification<Contribution, ApplicationUser, int>(User.UserId()))
                        .And(new CategoryTypeEqualsSpecification<Contribution>(CategoryType.School)),
                });
                if (result.Data.List is null)
                {
                    return Ok<ListDataSource<SchoolContributionInfoListResponseViewModel>>(new(result.Errors)
                    {
                        Data = new()
                    });
                }

                var schoolName = (await schoolService.Value.GetSchoolsNameAsync(new IdEqualsSpecification<School, long>(schoolId)))
                    .Data?.ElementAtOrDefault(0).Value;

                return Ok<ListDataSource<SchoolContributionInfoListResponseViewModel>>(new(result.Errors)
                {
                    Data = new()
                    {
                        List = result.Data.List.Select(t => new SchoolContributionInfoListResponseViewModel
                        {
                            Id = t.Id,
                            Comment = t.Comment,
                            Status = t.Status,
                            SchoolName = schoolName,
                        }),
                        TotalRecordsCount = result.Data.TotalRecordsCount,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ListDataSource<SchoolContributionInfoListResponseViewModel>>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpGet("{schoolId:long}/contributions/{contributionId:long}"), Produces<ApiResponse<SchoolContributionViewModel>>()]
        [Permission(policy: null)]
        public async Task<IActionResult<SchoolContributionViewModel>> GetSchoolContribution([FromRoute] long schoolId, [FromRoute] long contributionId)
        {
            try
            {
                var specification = new IdEqualsSpecification<Contribution, long>(contributionId)
                    .And(new IdentifierIdEqualsSpecification<Contribution>(schoolId))
                    .And(new CreationUserIdEqualsSpecification<Contribution, ApplicationUser, int>(User.UserId()))
                    .And(new CategoryTypeEqualsSpecification<Contribution>(CategoryType.School));
                var result = await contributionService.Value.GetContributionAsync(specification);

                SchoolContributionViewModel? viewModel = null;
                if (result.Data?.Data is not null)
                {
                    var dto = JsonSerializer.Deserialize<SchoolContributionDto>(result.Data.Data);
                    viewModel = dto is null ? null : MapFrom(dto);
                }

                return Ok<SchoolContributionViewModel>(new(result.Errors)
                {
                    Data = viewModel,
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<SchoolContributionViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpPost("{schoolId:long}/contributions"), Produces<ApiResponse<ManageSchoolContributionResponseViewModel>>()]
        [Permission(policy: null)]
        public async Task<IActionResult<ManageSchoolContributionResponseViewModel>> CreateSchoolContribution([FromRoute] long schoolId, [NotNull] SchoolContributionViewModel request)
        {
            try
            {
                var userId = User.UserId();
                var dto = MapTo(request, schoolId);
                var result = await schoolService.Value.ManageSchoolContributionAsync(new ManageSchoolContributionRequestDto
                {
                    UserId = userId,
                    SchoolId = schoolId,
                    Data = dto,
                });

                return Ok<ManageSchoolContributionResponseViewModel>(new(result.Errors)
                {
                    Data = new() { Id = result.Data, },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ManageSchoolContributionResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpPut("{schoolId:long}/contributions/{contributionId:long}"), Produces<ApiResponse<ManageSchoolContributionResponseViewModel>>()]
        [Permission(policy: null)]
        public async Task<IActionResult<ManageSchoolContributionResponseViewModel>> UpdateSchoolContribution([FromRoute] long schoolId, [FromRoute] long contributionId, [NotNull, FromBody] SchoolContributionViewModel request)
        {
            try
            {
                var dto = MapTo(request, schoolId);
                var result = await schoolService.Value.ManageSchoolContributionAsync(new ManageSchoolContributionRequestDto
                {
                    Id = contributionId,
                    SchoolId = schoolId,
                    Data = dto,
                });

                return Ok<ManageSchoolContributionResponseViewModel>(new(result.Errors)
                {
                    Data = new() { Id = result.Data, },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ManageSchoolContributionResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        #endregion

        #region Issues

        [HttpGet("{schoolId:long}/issues"), Produces<ApiResponse<ListDataSource<SchoolIssuesContributionResponseViewModel>>>()]
        [Permission(policy: null)]
        public async Task<IActionResult<ListDataSource<SchoolIssuesContributionResponseViewModel>>> GetSchoolIssuesContributionList([FromRoute] long schoolId, [NotNull, FromQuery] SchoolIssuesContributionListRequestViewModel request)
        {
            try
            {
                var result = await contributionService.Value.GetContributionsAsync(new ListRequestDto<Contribution>
                {
                    PagingDto = request.PagingDto,
                    Specification = new IdentifierIdEqualsSpecification<Contribution>(schoolId)
                        .And(new CreationUserIdEqualsSpecification<Contribution, ApplicationUser, int>(User.UserId()))
                        .And(new CategoryTypeEqualsSpecification<Contribution>(CategoryType.SchoolIssues)),
                }, true);

                if (result.Data.List is null)
                {
                    return Ok<ListDataSource<SchoolIssuesContributionResponseViewModel>>(new(result.Errors)
                    {
                        Data = new()
                    });
                }

                var schoolName = (await schoolService.Value.GetSchoolsNameAsync(new IdEqualsSpecification<School, long>(schoolId)))
                    .Data?.ElementAtOrDefault(0).Value;

                return Ok<ListDataSource<SchoolIssuesContributionResponseViewModel>>(new(result.Errors)
                {
                    Data = new()
                    {
                        List = result.Data.List.Select(t => new SchoolIssuesContributionResponseViewModel
                        {
                            Id = t.Id,
                            Comment = t.Comment,
                            Status = t.Status,
                            SchoolName = schoolName,
                            Description = t.Data,
                        }),
                        TotalRecordsCount = result.Data.TotalRecordsCount,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ListDataSource<SchoolIssuesContributionResponseViewModel>>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpPost("{schoolId:long}/issues"), Produces<ApiResponse<ManageSchoolIssuesContributionResponseViewModel>>()]
        [Permission(policy: null)]
        public async Task<IActionResult<ManageSchoolIssuesContributionResponseViewModel>> CreateSchoolIssuesContribution([FromRoute] long schoolId, [NotNull] ManageSchoolIssuesContributionRequestViewModel request)
        {
            try
            {
                var result = await schoolService.Value.CreateSchoolIssuesContributionAsync(new()
                {
                    Description = request.Description,
                    SchoolId = schoolId,
                    CreationUserId = User.UserId(),
                });

                return Ok<ManageSchoolIssuesContributionResponseViewModel>(new(result.Errors)
                {
                    Data = new() { Id = result.Data, },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ManageSchoolIssuesContributionResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        #endregion

        internal static SchoolContributionDto MapTo(SchoolContributionViewModel request, long schoolId) => new()
        {
            Address = request.Address,
            CityId = request.CityId,
            CountryId = request.CountryId,
            Email = request.Email,
            FaxNumber = request.FaxNumber,
            Id = schoolId,
            LocalAddress = request.LocalAddress,
            LocalName = request.LocalName,
            Name = request.Name,
            PhoneNumber = request.PhoneNumber,
            Quarter = request.Quarter,
            SchoolType = request.SchoolType,
            StateId = request.StateId,
            WebSite = request.WebSite,
            ZipCode = request.ZipCode,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            Tags = request.Tags,
        };

        internal static SchoolContributionViewModel MapFrom(SchoolContributionDto dto) => new()
        {
            Address = dto.Address,
            CityId = dto.CityId,
            CountryId = dto.CountryId,
            Email = dto.Email,
            FaxNumber = dto.FaxNumber,
            Latitude = dto.Latitude,
            LocalAddress = dto.LocalAddress,
            LocalName = dto.LocalName,
            Longitude = dto.Longitude,
            Name = dto.Name,
            PhoneNumber = dto.PhoneNumber,
            Quarter = dto.Quarter,
            SchoolType = dto.SchoolType,
            StateId = dto.StateId,
            WebSite = dto.WebSite,
            ZipCode = dto.ZipCode,
            Tags = dto.Tags,
        };
    }
}
