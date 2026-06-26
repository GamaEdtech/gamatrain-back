namespace GamaEdtech.Presentation.Api.Controllers
{
    using System;
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Domain.Specification.Identity;
    using GamaEdtech.Presentation.ViewModel.Connection;

    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;

    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    [Permission(policy: null)]
    public class ConnectionsController(Lazy<ILogger<ConnectionsController>> logger, Lazy<IConnectionService> connectionService)
        : ApiControllerBase<ConnectionsController>(logger)
    {
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
        public async Task<IActionResult<ListDataSource<FollowViewModel>>> Followers([FromRoute] long id, [NotNull, FromQuery] FollowersRequestViewModel request)
        {
            try
            {
                var result = await connectionService.Value.GetFollowersAsync(new()
                {
                    PagingDto = request.PagingDto,
                    Specification = new FollowingIdEqualsSpecification(id),
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
        public async Task<IActionResult<ListDataSource<FollowViewModel>>> Followings([FromRoute] long id, [NotNull, FromQuery] FollowingsRequestViewModel request)
        {
            try
            {
                var result = await connectionService.Value.GetFollowingsAsync(new()
                {
                    PagingDto = request.PagingDto,
                    Specification = new FollowerIdEqualsSpecification(id),
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
        public async Task<IActionResult<bool>> Follow([FromRoute] long id, [NotNull] FollowRequestViewModel request)
        {
            try
            {
                var result = await connectionService.Value.FollowAsync(new()
                {
                    ProfileId = id,
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
        public async Task<IActionResult<bool>> UnFollow([FromRoute] long id, [NotNull] UnFollowRequestViewModel request)
        {
            try
            {
                var result = await connectionService.Value.UnFollowAsync(new()
                {
                    ProfileId = id,
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
        public async Task<IActionResult<bool>> Subscribe([FromRoute] long id)
        {
            try
            {
                var result = await connectionService.Value.ToggleSubscriptionAsync(new()
                {
                    ProfileId = id,
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
    }
}

