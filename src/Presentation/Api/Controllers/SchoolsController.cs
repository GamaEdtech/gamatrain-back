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
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Data.Dto.School;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Domain.Specification;
    using GamaEdtech.Domain.Specification.School;
    using GamaEdtech.Presentation.ViewModel.Board;
    using GamaEdtech.Presentation.ViewModel.School;
    using GamaEdtech.Presentation.ViewModel.Tag;

    using Hangfire;

    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;

    using NetTopologySuite.Geometries;

    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    [Permission(policy: null)]
    public class SchoolsController(Lazy<ILogger<SchoolsController>> logger, Lazy<ISchoolService> schoolService
        , Lazy<IContributionService> contributionService, Lazy<IGlobalService> globalService, Lazy<ITagService> tagService)
        : ApiControllerBase<SchoolsController>(logger)
    {
        [HttpGet, Produces<ApiResponse<ListDataSource<SchoolInfoResponseViewModel>>>()]
        [AllowAnonymous]
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

                if (request.HasRating.HasValue)
                {
                    var specification = new HasRatingSpecification(request.HasRating.Value);
                    baseSpecification = baseSpecification is null ? specification : baseSpecification.And(specification);
                }

                if (request.HasImage.HasValue)
                {
                    var specification = new HasImageSpecification(request.HasImage.Value);
                    baseSpecification = baseSpecification is null ? specification : baseSpecification.And(specification);
                }

                if (request.Boards is not null)
                {
                    var specification = new BoardIdContainsSpecification(request.Boards);
                    baseSpecification = baseSpecification is null ? specification : baseSpecification.And(specification);
                }

                if (request.Tuition is not null)
                {
                    var specification = new TuitionSpecification(request.Tuition.Start, request.Tuition.End);
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
                            CityTitle = t.CityTitle,
                            CountryTitle = t.CountryTitle,
                            HasEmail = t.HasEmail,
                            HasPhone = t.HasPhoneNumber,
                            HasWebsite = t.HasWebSite,
                            HasLocation = t.Coordinates is not null,
                            Latitude = t.Coordinates?.Y,
                            Longitude = t.Coordinates?.X,
                            StateTitle = t.StateTitle,
                            DefaultImageUri = t.DefaultImageUri,
                            Distance = t.Distance,
                            CountryRank = t.CountryRank,
                            StateRank = t.StateRank,
                            CityRank = t.CityRank,
                            Rating = t.Rating,
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
        [AllowAnonymous]
        public async Task<IActionResult<SchoolResponseViewModel>> GetSchool([FromRoute] long id)
        {
            try
            {
                var specification = new IdEqualsSpecification<School, long>(id);
                var result = await schoolService.Value.GetSchoolAsync(specification);
                if (result.OperationResult is not Constants.OperationResult.Succeeded)
                {
                    return Ok<SchoolResponseViewModel>(new(result.Errors));
                }

                _ = BackgroundJob.Enqueue<ISchoolService>(t => t.IncreaseSchoolViewAsync(id));

                return Ok<SchoolResponseViewModel>(new()
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
                        DefaultImageUri = result.Data.DefaultImageUri,
                        Tuition = result.Data.Tuition,
                        Slug = result.Data.Name.Slugify(),
                        Description = result.Data.Description,
                        ViewCount = result.Data.ViewCount + 1,  //plus 1 for current view
                        Tags = result.Data.Tags?.Select(t => new TagResponseViewModel
                        {
                            Id = t.Id,
                            Icon = t.Icon,
                            Name = t.Name,
                            TagType = t.TagType,
                        }),
                        Boards = result.Data.Boards?.Select(t => new BoardsListResponseViewModel
                        {
                            Id = t.Id,
                            Icon = t.Icon,
                            Code = t.Code,
                            Title = t.Title,
                        }),
                        CountryRank = result.Data.CountryRank,
                        StateRank = result.Data.StateRank,
                        CityRank = result.Data.CityRank,
                        Rating = result.Data.Rating,
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
        [AllowAnonymous]
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
        [AllowAnonymous]
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

                return Ok<ListDataSource<SchoolCommentsResponseViewModel>>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpPost("{schoolId:long}/comments"), Produces<ApiResponse<ManageSchoolCommentResponseViewModel>>()]
        public async Task<IActionResult<ManageSchoolCommentResponseViewModel>> CreateSchoolComment([FromRoute] long schoolId, [NotNull] ManageSchoolCommentRequestViewModel request)
        {
            try
            {
                var validateCaptcha = await globalService.Value.VerifyCaptchaAsync(request.Captcha);
                if (!validateCaptcha.Data)
                {
                    return Ok<ManageSchoolCommentResponseViewModel>(new(new Error { Message = "Invalid Captcha" }));
                }

                var result = await schoolService.Value.CreateSchoolCommentContributionAsync(new()
                {
                    UserId = User.UserId(),
                    SchoolId = schoolId,
                    CommentContribution = new()
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
                    }
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

        [HttpGet("{schoolId:long}/images/{fileType:ImageFileType}"), Produces<ApiResponse<IEnumerable<SchoolImageInfoViewModel>>>()]
        [AllowAnonymous]
        public async Task<IActionResult<IEnumerable<SchoolImageInfoViewModel>>> GetSchoolImagesList([FromRoute] long schoolId, [FromRoute] ImageFileType fileType)
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
                        IsDefault = t.IsDefault,
                    }),
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<IEnumerable<SchoolImageInfoViewModel>>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpPost("{schoolId:long}/images"), Produces<ApiResponse<CreateSchoolImageResponseViewModel>>()]
        public async Task<IActionResult<CreateSchoolImageResponseViewModel>> CreateSchoolImageContribution([FromRoute] long schoolId, [NotNull, FromForm] CreateSchoolImageRequestViewModel request)
        {
            try
            {
                var result = await schoolService.Value.CreateSchoolImageContributionAsync(new ManageSchoolImageContributionRequestDto
                {
                    File = request.File!,
                    FileType = request.FileType!,
                    SchoolId = schoolId,
                    TagId = request.TagId,
                    IsDefault = request.IsDefault,
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

        [HttpDelete("{schoolId:long}/images/{contributionId:long}"), Produces<ApiResponse<bool>>()]
        [Display(Name = "Removing an Image by creator")]
        public async Task<IActionResult<bool>> RemoveMySchoolImageContribution([FromRoute] long schoolId, [FromRoute] long contributionId)
        {
            try
            {
                var specification = new ContributionIdEqualsSpecification(contributionId)
                    .And(new SchoolIdEqualsSpecification<SchoolImage>(schoolId))
                    .And(new CreationUserIdEqualsSpecification<SchoolImage, ApplicationUser, long>(User.UserId()));
                var result = await schoolService.Value.RemoveSchoolImageAsync(specification);
                return Ok(new ApiResponse<bool>
                {
                    Errors = result.Errors,
                    Data = result.Data
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok(new ApiResponse<bool> { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpPost("{schoolId:long}/images/{imageId:long}/contributions"), Produces<ApiResponse<bool>>()]
        [Display(Name = "Request Removing an Image as a Contribution")]
        public async Task<IActionResult<RemoveSchoolImageContributionResponseViewModel>> RemoveSchoolImageContribution([FromRoute] long schoolId, [FromRoute] long imageId, [NotNull] RemoveSchoolImageContributionRequestViewModel request)
        {
            try
            {
                var result = await schoolService.Value.CreateRemoveSchoolImageContributionAsync(new()
                {
                    Description = request.Description,
                    SchoolId = schoolId,
                    ImageId = imageId,
                    CreationUserId = User.UserId(),
                });

                return Ok<RemoveSchoolImageContributionResponseViewModel>(new(result.Errors)
                {
                    Data = new() { Id = result.Data, },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<RemoveSchoolImageContributionResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        #endregion

        #region Contributions

        [HttpGet("{schoolId:long}/contributions"), Produces<ApiResponse<ListDataSource<SchoolContributionInfoListResponseViewModel>>>()]
        public async Task<IActionResult<ListDataSource<SchoolContributionInfoListResponseViewModel>>> GetSchoolContributionList([FromRoute] long schoolId, [NotNull, FromQuery] SchoolContributionListRequestViewModel request)
        {
            try
            {
                var result = await contributionService.Value.GetContributionsAsync<SchoolContributionDto>(new ListRequestDto<Contribution>
                {
                    PagingDto = request.PagingDto,
                    Specification = new IdentifierIdEqualsSpecification<Contribution>(schoolId)
                        .And(new CreationUserIdEqualsSpecification<Contribution, ApplicationUser, long>(User.UserId()))
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
        public async Task<IActionResult<SchoolContributionViewModel>> GetSchoolContribution([FromRoute] long schoolId, [FromRoute] long contributionId)
        {
            try
            {
                var specification = new IdEqualsSpecification<Contribution, long>(contributionId)
                    .And(new IdentifierIdEqualsSpecification<Contribution>(schoolId))
                    .And(new CreationUserIdEqualsSpecification<Contribution, ApplicationUser, long>(User.UserId()))
                    .And(new CategoryTypeEqualsSpecification<Contribution>(CategoryType.School));
                var result = await contributionService.Value.GetContributionAsync<SchoolContributionDto>(specification);

                SchoolContributionViewModel? viewModel = null;
                if (result.Data?.Data is not null)
                {
                    viewModel = MapFrom(result.Data.Data);
                    var tags = await GetTagsAsync(result.Data.Data.Tags);
                    viewModel!.Tags = tags;
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
        public async Task<IActionResult<ManageSchoolContributionResponseViewModel>> CreateSchoolContribution([FromRoute] long schoolId, [NotNull, FromBody] ManageSchoolContributionRequestViewModel request)
        {
            try
            {
                var result = await schoolService.Value.ManageSchoolContributionAsync(new ManageSchoolContributionRequestDto
                {
                    UserId = User.UserId(),
                    SchoolId = schoolId,
                    SchoolContribution = new()
                    {
                        Address = request.Address,
                        CityId = request.CityId,
                        CountryId = request.CountryId,
                        Email = request.Email,
                        FaxNumber = request.FaxNumber,
                        Latitude = request.Latitude,
                        LocalAddress = request.LocalAddress,
                        LocalName = request.LocalName,
                        Longitude = request.Longitude,
                        Name = request.Name,
                        PhoneNumber = request.PhoneNumber,
                        Quarter = request.Quarter,
                        SchoolType = request.SchoolType,
                        StateId = request.StateId,
                        WebSite = request.WebSite,
                        ZipCode = request.ZipCode,
                        Tags = request.Tags,
                        Boards = request.Boards,
                        DefaultImageId = request.DefaultImageId,
                        Tuition = request.Tuition,
                        Description = request.Description,
                        LocalizedValues = request.LocalizedValues?.Select(t => new SchoolLocalizedValueDto
                        {
                            LanguageId = t.LanguageId.GetValueOrDefault(),
                            Description = t.Description,
                            Name = t.Name,
                            Address = t.Address,
                        }),
                    },
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
        public async Task<IActionResult<ManageSchoolContributionResponseViewModel>> UpdateSchoolContribution([FromRoute] long schoolId, [FromRoute] long contributionId, [NotNull, FromBody] ManageSchoolContributionRequestViewModel request)
        {
            try
            {
                var result = await schoolService.Value.ManageSchoolContributionAsync(new ManageSchoolContributionRequestDto
                {
                    Id = contributionId,
                    UserId = User.UserId(),
                    SchoolId = schoolId,
                    SchoolContribution = new()
                    {
                        Address = request.Address,
                        CityId = request.CityId,
                        CountryId = request.CountryId,
                        Email = request.Email,
                        FaxNumber = request.FaxNumber,
                        Latitude = request.Latitude,
                        LocalAddress = request.LocalAddress,
                        LocalName = request.LocalName,
                        Longitude = request.Longitude,
                        Name = request.Name,
                        PhoneNumber = request.PhoneNumber,
                        Quarter = request.Quarter,
                        SchoolType = request.SchoolType,
                        StateId = request.StateId,
                        WebSite = request.WebSite,
                        ZipCode = request.ZipCode,
                        Tags = request.Tags,
                        Boards = request.Boards,
                        DefaultImageId = request.DefaultImageId,
                        Tuition = request.Tuition,
                        Description = request.Description,
                        LocalizedValues = request.LocalizedValues?.Select(t => new SchoolLocalizedValueDto
                        {
                            LanguageId = t.LanguageId.GetValueOrDefault(),
                            Description = t.Description,
                            Name = t.Name,
                            Address = t.Address,
                        }),
                    },
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

        [HttpPost("contributions"), Produces<ApiResponse<ManageSchoolContributionResponseViewModel>>()]
        public async Task<IActionResult<ManageSchoolContributionResponseViewModel>> CreateNewSchoolContribution([NotNull, FromForm] ManageNewSchoolContributionRequestViewModel request)
        {
            try
            {
                var result = await schoolService.Value.ManageSchoolContributionAsync(new()
                {
                    UserId = User.UserId(),
                    File = request.File,
                    IsDefault = true,
                    SchoolContribution = new()
                    {
                        Address = request.Address,
                        CityId = request.CityId,
                        CountryId = request.CountryId,
                        Email = request.Email,
                        FaxNumber = request.FaxNumber,
                        Latitude = request.Latitude,
                        LocalAddress = request.LocalAddress,
                        LocalName = request.LocalName,
                        Longitude = request.Longitude,
                        Name = request.Name,
                        PhoneNumber = request.PhoneNumber,
                        Quarter = request.Quarter,
                        SchoolType = request.SchoolType,
                        StateId = request.StateId,
                        WebSite = request.WebSite,
                        ZipCode = request.ZipCode,
                        Tags = request.Tags,
                        Boards = request.Boards,
                        Tuition = request.Tuition,
                        Description = request.Description,
                        Comment = request.Comment is null ? null : new()
                        {
                            ArtisticActivitiesRate = request.Comment.ArtisticActivitiesRate,
                            BehaviorRate = request.Comment.BehaviorRate,
                            ClassesQualityRate = request.Comment.ClassesQualityRate,
                            Comment = request.Comment.Comment,
                            EducationRate = request.Comment.EducationRate,
                            FacilitiesRate = request.Comment.FacilitiesRate,
                            ITTrainingRate = request.Comment.ITTrainingRate,
                            SafetyAndHappinessRate = request.Comment.SafetyAndHappinessRate,
                            TuitionRatioRate = request.Comment.TuitionRatioRate,
                            CreationDate = DateTimeOffset.UtcNow,
                            CreationUserId = User.UserId(),
                        },
                        LocalizedValues = request.LocalizedValues?.Select(t => new SchoolLocalizedValueDto
                        {
                            LanguageId = t.LanguageId.GetValueOrDefault(),
                            Description = t.Description,
                            Name = t.Name,
                            Address = t.Address,
                        }),
                    },
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
        public async Task<IActionResult<ListDataSource<SchoolIssuesContributionResponseViewModel>>> GetSchoolIssuesContributionList([FromRoute] long schoolId, [NotNull, FromQuery] SchoolIssuesContributionListRequestViewModel request)
        {
            try
            {
                var result = await contributionService.Value.GetContributionsAsync<string>(new ListRequestDto<Contribution>
                {
                    PagingDto = request.PagingDto,
                    Specification = new IdentifierIdEqualsSpecification<Contribution>(schoolId)
                        .And(new CreationUserIdEqualsSpecification<Contribution, ApplicationUser, long>(User.UserId()))
                        .And(new CategoryTypeEqualsSpecification<Contribution>(CategoryType.SchoolIssues)),
                }, false);

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

        private async Task<IEnumerable<TagResponseViewModel>?> GetTagsAsync(IEnumerable<long>? tagIds)
        {
            if (tagIds?.Any() != true)
            {
                return null;
            }

            var result = await tagService.Value.GetTagsAsync(new ListRequestDto<Tag> { Specification = new IdContainsSpecification<Tag, long>(tagIds) });
            return result.Data.List?.Select(t => new TagResponseViewModel
            {
                Id = t.Id,
                Name = t.Name,
                Icon = t.Icon,
                TagType = t.TagType,
            });
        }

        internal static SchoolContributionViewModel? MapFrom(SchoolContributionDto? dto) => dto is null
                ? null
                : new()
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
                    Boards = dto.Boards,
                    Tuition = dto.Tuition,
                    Description = dto.Description,
                    LocalizedValues = dto.LocalizedValues?.Select(t => new SchoolLocalizedValueViewModel
                    {
                        LanguageId = t.LanguageId,
                        Description = t.Description,
                        Name = t.Name,
                        Address = t.Address,
                    }),
                };
    }
}
