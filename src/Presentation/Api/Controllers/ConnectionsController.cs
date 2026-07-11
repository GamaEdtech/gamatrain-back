namespace GamaEdtech.Presentation.Api.Controllers
{
    using System;
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Domain.Specification.Identity;
    using GamaEdtech.Presentation.ViewModel.Connection;

    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    [Permission(policy: null)]
    public class ConnectionsController(Lazy<ILogger<ConnectionsController>> logger, Lazy<IConnectionService> connectionService, Lazy<IIdentityService> identityService)
        : ApiControllerBase<ConnectionsController>(logger)
    {
        /// <summary>
        /// Resolves a users/{id}/... route id, optionally a legacy CoreId (idType=CoreId), to a local user id.
        /// idType is a plain string (not IdentifierType directly) because Swashbuckle expands a query-bound
        /// smart-enum parameter into its internal properties (Name, Value, ...) instead of a single named
        /// parameter - no [FromQuery] smart enum existed anywhere else in this codebase to hit this before.
        /// </summary>
        private async Task<ResultData<long>> ResolveTargetIdAsync(long id, string? idType)
        {
            var type = idType.TryGetFromNameOrValue<IdentifierType, byte>(out var parsed) ? parsed! : IdentifierType.Id;
            return await identityService.Value.ResolveUserIdAsync(id, type);
        }

        [HttpGet("requests"), Produces(typeof(ApiResponse<ListDataSource<FollowRequestsResponseViewModel>>))]
        [Display(Name = "Get Follow Requests List")]
        public async Task<IActionResult<ListDataSource<FollowRequestsResponseViewModel>>> Requests([NotNull, FromQuery] FollowRequestsRequestViewModel request)
        {
            try
            {
                var result = await connectionService.Value.GetFollowRequestsAsync(new()
                {
                    UserId = User.UserId(),
                    PagingDto = request.PagingDto,
                });

                return Ok<ListDataSource<FollowRequestsResponseViewModel>>(new(result.Errors)
                {
                    Data = result.Data.List is null ? new() : new()
                    {
                        List = result.Data.List.Select(t => new FollowRequestsResponseViewModel
                        {
                            Id = t.Id,
                            UserId = t.UserId,
                            AvatarUri = t.AvatarUri,
                            Name = t.Name,
                        }),
                        TotalRecordsCount = result.Data.TotalRecordsCount,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ListDataSource<FollowRequestsResponseViewModel>>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpPatch("{id:long}/confirm"), Produces(typeof(ApiResponse<bool>))]
        public async Task<IActionResult<bool>> ConfirmFollowRequest([FromRoute] long id, [NotNull] ConfirmFollowRequestRequestViewModel request)
        {
            try
            {
                var result = await connectionService.Value.ConfirmFollowRequestAsync(new()
                {
                    Id = id,
                    TwoWay = request.TwoWay,
                    UserId = User.UserId(),
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

        [HttpPatch("{id:long}/reject"), Produces(typeof(ApiResponse<bool>))]
        public async Task<IActionResult<bool>> RejectFollowRequest([FromRoute] long id)
        {
            try
            {
                var result = await connectionService.Value.RejectFollowRequestAsync(new()
                {
                    Id = id,
                    UserId = User.UserId(),
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

        [HttpGet("users/{id:long}/followers"), Produces(typeof(ApiResponse<ListDataSource<FollowViewModel>>))]
        [Display(Name = "Get List of Followers of a User")]
        public async Task<IActionResult<ListDataSource<FollowViewModel>>> Followers([FromRoute] long id, [NotNull, FromQuery] FollowersRequestViewModel request, [FromQuery] string? idType = null)
        {
            try
            {
                var resolved = await ResolveTargetIdAsync(id, idType);
                if (resolved.OperationResult is not OperationResult.Succeeded)
                {
                    return Ok<ListDataSource<FollowViewModel>>(new(resolved.Errors));
                }

                var result = await connectionService.Value.GetFollowersAsync(new()
                {
                    PagingDto = request.PagingDto,
                    Specification = new FollowingIdEqualsSpecification(resolved.Data),
                });

                return Ok<ListDataSource<FollowViewModel>>(new(result.Errors)
                {
                    Data = result.Data.List is null ? new() : new()
                    {
                        List = result.Data.List.Select(t => new FollowViewModel
                        {
                            UserId = t.UserId,
                            AvatarUri = t.AvatarUri,
                            Name = t.Name,
                        }),
                        TotalRecordsCount = result.Data.TotalRecordsCount,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ListDataSource<FollowViewModel>>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpGet("users/{id:long}/followings"), Produces(typeof(ApiResponse<ListDataSource<FollowViewModel>>))]
        [Display(Name = "Get List of Users that a user follow")]
        public async Task<IActionResult<ListDataSource<FollowViewModel>>> Followings([FromRoute] long id, [NotNull, FromQuery] FollowingsRequestViewModel request, [FromQuery] string? idType = null)
        {
            try
            {
                var resolved = await ResolveTargetIdAsync(id, idType);
                if (resolved.OperationResult is not OperationResult.Succeeded)
                {
                    return Ok<ListDataSource<FollowViewModel>>(new(resolved.Errors));
                }

                var result = await connectionService.Value.GetFollowingsAsync(new()
                {
                    PagingDto = request.PagingDto,
                    Specification = new FollowerIdEqualsSpecification(resolved.Data),
                });

                return Ok<ListDataSource<FollowViewModel>>(new(result.Errors)
                {
                    Data = result.Data.List is null ? new() : new()
                    {
                        List = result.Data.List.Select(t => new FollowViewModel
                        {
                            UserId = t.UserId,
                            AvatarUri = t.AvatarUri,
                            Name = t.Name,
                        }),
                        TotalRecordsCount = result.Data.TotalRecordsCount,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ListDataSource<FollowViewModel>>(new(new Error { Message = exc.Message }));
            }
        }

        [HttpPost("users/{id:long}/follow"), Produces(typeof(ApiResponse<bool>))]
        [Display(Name = "follow a user")]
        public async Task<IActionResult<bool>> Follow([FromRoute] long id, [NotNull] FollowRequestViewModel request, [FromQuery] string? idType = null)
        {
            try
            {
                var resolved = await ResolveTargetIdAsync(id, idType);
                if (resolved.OperationResult is not OperationResult.Succeeded)
                {
                    return Ok<bool>(new(resolved.Errors));
                }

                var result = await connectionService.Value.FollowAsync(new()
                {
                    ProfileId = resolved.Data,
                    UserId = User.UserId(),
                    SubscribeToActivityFeed = request.SubscribeToActivityFeed,
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

        [HttpPost("users/{id:long}/unfollow"), Produces(typeof(ApiResponse<bool>))]
        [Display(Name = "Unfollow a user")]
        public async Task<IActionResult<bool>> UnFollow([FromRoute] long id, [NotNull] UnFollowRequestViewModel request, [FromQuery] string? idType = null)
        {
            try
            {
                var resolved = await ResolveTargetIdAsync(id, idType);
                if (resolved.OperationResult is not OperationResult.Succeeded)
                {
                    return Ok<bool>(new(resolved.Errors));
                }

                var result = await connectionService.Value.UnFollowAsync(new()
                {
                    ProfileId = resolved.Data,
                    UserId = User.UserId(),
                    TwoWayRevoke = request.TwoWayRevoke,
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

        [HttpPatch("users/{id:long}/subscriptions/toggle"), Produces(typeof(ApiResponse<bool>))]
        [Display(Name = "Subscribe/UnSubscribe to activity feed of a user")]
        public async Task<IActionResult<bool>> Subscribe([FromRoute] long id, [FromQuery] string? idType = null)
        {
            try
            {
                var resolved = await ResolveTargetIdAsync(id, idType);
                if (resolved.OperationResult is not OperationResult.Succeeded)
                {
                    return Ok<bool>(new(resolved.Errors));
                }

                var result = await connectionService.Value.ToggleSubscriptionAsync(new()
                {
                    ProfileId = resolved.Data,
                    UserId = User.UserId(),
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

        [HttpPost("status"), Produces(typeof(ApiResponse<IEnumerable<ConnectionStatusResponseViewModel>>))]
        [Display(Name = "Check whether the current user follows each of a list of users")]
        public async Task<IActionResult<IEnumerable<ConnectionStatusResponseViewModel>>> Status([NotNull] ConnectionStatusRequestViewModel request)
        {
            try
            {
                var idType = request.IdType ?? IdentifierType.Id;
                var resolveResult = await identityService.Value.ResolveUserIdsAsync(request.Ids!, idType);
                if (resolveResult.OperationResult is not OperationResult.Succeeded)
                {
                    return Ok<IEnumerable<ConnectionStatusResponseViewModel>>(new(resolveResult.Errors));
                }

                var idMap = resolveResult.Data!;
                var result = await connectionService.Value.GetConnectionStatusAsync(new()
                {
                    UserId = User.UserId(),
                    TargetIds = idMap.Values,
                });
                if (result.OperationResult is not OperationResult.Succeeded)
                {
                    return Ok<IEnumerable<ConnectionStatusResponseViewModel>>(new(result.Errors));
                }

                var followingLocalIds = new HashSet<long>(result.Data!.Where(t => t.IsFollowing).Select(t => t.Id));
                var data = request.Ids!.Select(originalId => new ConnectionStatusResponseViewModel
                {
                    Id = originalId,
                    IsFollowing = idMap.TryGetValue(originalId, out var localId) && followingLocalIds.Contains(localId),
                });

                return Ok<IEnumerable<ConnectionStatusResponseViewModel>>(new(result.Errors) { Data = data });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<IEnumerable<ConnectionStatusResponseViewModel>>(new(new Error { Message = exc.Message }));
            }
        }
    }
}

