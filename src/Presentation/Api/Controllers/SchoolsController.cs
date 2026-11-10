namespace GamaEdtech.Presentation.Api.Controllers
{
    using System.Diagnostics.CodeAnalysis;
    using System.Text.Json;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Data.Enumeration;
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
        , Lazy<IContributionService> contributionService, Lazy<IGlobalService> globalService, Lazy<ITagService> tagService
        , Lazy<ISchoolAiService> schoolAiService)
        : ApiControllerBase<SchoolsController>(logger)
    {
        [HttpGet, Produces<ApiResponse<ListDataSource<SchoolInfoResponseViewModel>>>()]
        // Public and the same for every visitor: Cloudflare (api_get rule) serves repeats from the edge,
        // so a traffic spike on the same lists and searches doesn't reach the database.
        [ResponseCache(Location = ResponseCacheLocation.Any, Duration = 120)]
        [AllowAnonymous]
        public async Task<IActionResult<ListDataSource<SchoolInfoResponseViewModel>>> GetSchools([NotNull, FromQuery] SchoolInfoRequestViewModel request)
        {
            try
            {
                var baseSpecification = BuildSchoolSpecification(request, true, out var point);

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
                            SchoolType = t.SchoolType,
                            ReviewCount = t.ReviewCount,
                            TagIds = t.TagIds,
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

        [HttpGet("facets"), Produces<ApiResponse<SchoolFacetsResponseViewModel>>()]
        // Same filters as the list, same edge caching (see GetSchools).
        [ResponseCache(Location = ResponseCacheLocation.Any, Duration = 300)]
        [AllowAnonymous]
        public async Task<IActionResult<SchoolFacetsResponseViewModel>> GetSchoolFacets([NotNull, FromQuery] SchoolFacetsRequestViewModel request)
        {
            try
            {
                var specification = BuildSchoolSpecification(request, true, out _);
                var result = await schoolService.Value.GetSchoolFacetsAsync(specification, request.IncludeRegions ? ChildRegionLevel(request) : null);
                return result.OperationResult is not Constants.OperationResult.Succeeded || result.Data is null
                    ? Ok<SchoolFacetsResponseViewModel>(new(result.Errors))
                    : Ok<SchoolFacetsResponseViewModel>(new()
                    {
                        Data = new()
                        {
                            Total = result.Data.Total,
                            WithImage = result.Data.WithImage,
                            WithRating = result.Data.WithRating,
                            WithTuition = result.Data.WithTuition,
                            SchoolTypes = result.Data.SchoolTypes?.Select(t => new SchoolTypeFacetViewModel { SchoolType = t.SchoolType, Count = t.Count }),
                            Tags = result.Data.Tags?.Select(t => new SchoolTagFacetViewModel { Id = t.Id, Name = t.Name, Icon = t.Icon, Count = t.Count }),
                            RegionLevel = result.Data.RegionLevel?.ToString().ToLowerInvariant(),
                            Regions = result.Data.Regions?.Select(t => new SchoolRegionFacetViewModel { Id = t.Id, Title = t.Title, Count = t.Count }),
                        },
                    });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<SchoolFacetsResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpGet("map/{zoom:int}/{x:int}/{y:int}"), Produces<ApiResponse<SchoolMapTileResponseViewModel>>()]
        // One fixed tile per URL: everyone looking at the same place asks the same URLs, so the edge answers repeats.
        [ResponseCache(Location = ResponseCacheLocation.Any, Duration = 600)]
        [AllowAnonymous]
        public async Task<IActionResult<SchoolMapTileResponseViewModel>> GetSchoolMapTile([FromRoute] int zoom, [FromRoute] int x, [FromRoute] int y, [NotNull, FromQuery] SchoolFilterRequestViewModel request)
        {
            try
            {
                // The tile is the area: a radius or box in the query would only make otherwise identical tiles uncacheable.
                var specification = BuildSchoolSpecification(request, false, out _);
                var result = await schoolService.Value.GetSchoolMapTileAsync(specification, zoom, x, y);
                return result.OperationResult is not Constants.OperationResult.Succeeded || result.Data is null
                    ? Ok<SchoolMapTileResponseViewModel>(new(result.Errors))
                    : Ok<SchoolMapTileResponseViewModel>(new()
                    {
                        Data = new()
                        {
                            Total = result.Data.Total,
                            Clusters = result.Data.Clusters?.Select(t => new SchoolMapClusterViewModel { Latitude = t.Latitude, Longitude = t.Longitude, Count = t.Count }),
                            Schools = result.Data.Schools?.Select(t => new SchoolMapPointViewModel
                            {
                                Id = t.Id,
                                Name = t.Name,
                                Slug = t.Name.Slugify(),
                                Latitude = t.Latitude,
                                Longitude = t.Longitude,
                                Rating = t.Rating,
                                ReviewCount = t.ReviewCount,
                                HasImage = t.HasImage,
                                ImageUri = t.ImageUri,
                            }),
                        },
                    });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<SchoolMapTileResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpGet("compare"), Produces<ApiResponse<IEnumerable<SchoolCompareResponseViewModel>>>()]
        [ResponseCache(Location = ResponseCacheLocation.Any, Duration = 300)]
        [AllowAnonymous]
        public async Task<IActionResult<IEnumerable<SchoolCompareResponseViewModel>>> CompareSchools([FromQuery] IEnumerable<long>? ids)
        {
            try
            {
                var list = ids?.Distinct().ToList() ?? [];
                if (list.Count is 0 or > SchoolCompareDto.MaxSchools)
                {
                    return Ok<IEnumerable<SchoolCompareResponseViewModel>>(new(new Error { Message = $"Pick 1 to {SchoolCompareDto.MaxSchools} schools to compare." }));
                }

                var result = await schoolService.Value.GetSchoolsForCompareAsync(list);
                return result.OperationResult is not Constants.OperationResult.Succeeded || result.Data is null
                    ? Ok<IEnumerable<SchoolCompareResponseViewModel>>(new(result.Errors))
                    : Ok<IEnumerable<SchoolCompareResponseViewModel>>(new()
                    {
                        Data = result.Data.Select(t => new SchoolCompareResponseViewModel
                        {
                            School = ToSchoolResponse(t.School, 0),
                            Rate = t.Rate is null ? null : new()
                            {
                                ArtisticActivitiesRate = t.Rate.ArtisticActivitiesRate,
                                AverageRate = t.Rate.AverageRate,
                                BehaviorRate = t.Rate.BehaviorRate,
                                ClassesQualityRate = t.Rate.ClassesQualityRate,
                                EducationRate = t.Rate.EducationRate,
                                FacilitiesRate = t.Rate.FacilitiesRate,
                                ITTrainingRate = t.Rate.ITTrainingRate,
                                SafetyAndHappinessRate = t.Rate.SafetyAndHappinessRate,
                                TuitionRatioRate = t.Rate.TuitionRatioRate,
                                TotalCount = t.Rate.TotalCount,
                            },
                        }).ToList(),
                    });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<IEnumerable<SchoolCompareResponseViewModel>>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpGet("{id:long}"), Produces<ApiResponse<SchoolResponseViewModel>>()]
        [ResponseCache(Location = ResponseCacheLocation.Any, Duration = 300)]
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
                    // plus 1 for the current view
                    Data = result.Data is null ? null : ToSchoolResponse(result.Data, 1),
                });
            }

            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<SchoolResponseViewModel>(new(new Error { Message = exc.Message }));
            }
        }

        /// <summary>The level one below the location filter: a city filter has none.</summary>
        private static SchoolRegionLevel? ChildRegionLevel(SchoolFilterRequestViewModel request) => (request.CityId, request.StateId, request.CountryId) switch
        {
            (not null, _, _) => null,
            (null, not null, _) => SchoolRegionLevel.City,
            (null, null, not null) => SchoolRegionLevel.State,
            _ => SchoolRegionLevel.Country,
        };

        private static SchoolResponseViewModel ToSchoolResponse(SchoolDto dto, long extraViews) => new()
        {
            Id = dto.Id,
            Address = dto.Address,
            LocalAddress = dto.LocalAddress,
            Name = dto.Name,
            LocalName = dto.LocalName,
            SchoolType = dto.SchoolType,
            StateId = dto.StateId,
            StateTitle = dto.StateTitle,
            ZipCode = dto.ZipCode,
            Latitude = dto.Coordinates?.Y,
            Longitude = dto.Coordinates?.X,
            WebSite = dto.WebSite,
            Email = dto.Email,
            CityId = dto.CityId,
            CityTitle = dto.CityTitle,
            CountryId = dto.CountryId,
            CountryTitle = dto.CountryTitle,
            FaxNumber = dto.FaxNumber,
            PhoneNumber = dto.PhoneNumber,
            Quarter = dto.Quarter,
            OsmId = dto.OsmId,
            DefaultImageUri = dto.DefaultImageUri,
            Tuition = dto.Tuition,
            Slug = dto.Name.Slugify(),
            Description = dto.Description,
            ViewCount = dto.ViewCount + extraViews,
            Tags = dto.Tags?.Select(t => new TagResponseViewModel
            {
                Id = t.Id,
                Icon = t.Icon,
                Name = t.Name,
                TagType = t.TagType,
            }),
            Boards = dto.Boards?.Select(t => new BoardsListResponseViewModel
            {
                Id = t.Id,
                Icon = t.Icon,
                Code = t.Code,
                Title = t.Title,
            }),
            CountryRank = dto.CountryRank,
            StateRank = dto.StateRank,
            CityRank = dto.CityRank,
            Rating = dto.Rating,
        };

        /// <summary>
        /// The specification for the school list's filters. <paramref name="includeArea"/> adds the radius
        /// (<c>Location</c>) and box (<c>Bounds</c>) filters; <paramref name="point"/> is the radius centre, for distances.
        /// </summary>
        private static ISpecification<School>? BuildSchoolSpecification(SchoolFilterRequestViewModel request, bool includeArea, out Point? point)
        {
            ISpecification<School>? specification = null;
            void Add(ISpecification<School> item) => specification = specification is null ? item : specification.And(item);

            if (request.CountryId.HasValue)
            {
                Add(new CountryIdEqualsSpecification(request.CountryId.Value));
            }

            if (request.StateId.HasValue)
            {
                Add(new StateIdEqualsSpecification(request.StateId.Value));
            }

            if (request.CityId.HasValue)
            {
                Add(new CityIdEqualsSpecification(request.CityId.Value));
            }

            if (!string.IsNullOrEmpty(request.Name))
            {
                Add(new NameContainsSpecification(request.Name));
            }

            if (request.HasRating.HasValue)
            {
                Add(new HasRatingSpecification(request.HasRating.Value));
            }

            if (request.HasImage.HasValue)
            {
                Add(new HasImageSpecification(request.HasImage.Value));
            }

            if (request.Boards is not null)
            {
                Add(new BoardIdContainsSpecification(request.Boards));
            }

            if (request.Tuition is not null)
            {
                Add(new TuitionSpecification(request.Tuition.Start, request.Tuition.End));
            }

            // Any of the picked types. Unknown names are ignored rather than matching nothing.
            ISpecification<School>? types = null;
            foreach (var item in request.SchoolTypes?.SelectMany(t => t.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) ?? [])
            {
                if (item.TryGetFromNameOrValue<SchoolType, byte>(out var schoolType) && schoolType is not null)
                {
                    var typeSpecification = new SchoolTypeEqualsSpecification(schoolType);
                    types = types is null ? typeSpecification : types.Or(typeSpecification);
                }
            }

            if (types is not null)
            {
                Add(types);
            }

            foreach (var tagId in request.Tags?.Distinct() ?? [])
            {
                Add(new HasTagSpecification(tagId));
            }

            point = null;
            if (includeArea && request.Location is not null)
            {
                var location = new LocationIncludeSpecification(request.Location.Latitude!.Value, request.Location.Longitude!.Value, request.Location.Radius!.Value);
                point = location.Point;
                Add(location);
            }

            if (includeArea && request.Bounds is not null)
            {
                Add(new WithinBoundsSpecification(request.Bounds.South!.Value, request.Bounds.West!.Value, request.Bounds.North!.Value, request.Bounds.East!.Value));
            }

            return specification;
        }

        #region Comments

        [HttpGet("{schoolId:long}/rate"), Produces<ApiResponse<SchoolRateResponseViewModel>>()]
        [ResponseCache(Location = ResponseCacheLocation.Any, Duration = 300)]
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

        /// <summary>An AI draft of a comment and ratings for the school, for the signed-in user to edit before posting.</summary>
        [HttpPost("{schoolId:long}/comments/ai-draft"), Produces<ApiResponse<SchoolAiCommentResponseViewModel>>()]
        public async Task<IActionResult<SchoolAiCommentResponseViewModel>> DraftSchoolCommentWithAi([FromRoute] long schoolId)
        {
            try
            {
                var result = await schoolAiService.Value.DraftCommentAsync(schoolId);
                return Ok<SchoolAiCommentResponseViewModel>(new(result.Errors)
                {
                    Data = result.Data is null ? null : new()
                    {
                        Comment = result.Data.Comment,
                        ClassesQualityRate = result.Data.ClassesQualityRate,
                        EducationRate = result.Data.EducationRate,
                        ITTrainingRate = result.Data.ITTrainingRate,
                        SafetyAndHappinessRate = result.Data.SafetyAndHappinessRate,
                        BehaviorRate = result.Data.BehaviorRate,
                        TuitionRatioRate = result.Data.TuitionRatioRate,
                        FacilitiesRate = result.Data.FacilitiesRate,
                        ArtisticActivitiesRate = result.Data.ArtisticActivitiesRate,
                    },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<SchoolAiCommentResponseViewModel>(new(new Error { Message = exc.Message }));
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
        [ResponseCache(Location = ResponseCacheLocation.Any, Duration = 300)]
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
                }, true);

                if (result.Data.List is null)
                {
                    return Ok<ListDataSource<SchoolIssuesContributionResponseViewModel>>(new(result.Errors)
                    {
                        Data = new()
                    });
                }

                var schoolName = (await schoolService.Value.GetSchoolsNameAsync(new IdEqualsSpecification<School, long>(schoolId), includeDeleted: true))
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
