namespace GamaEdtech.Application.Service
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Core.Extensions.Collections.Generic;
    using GamaEdtech.Common.Core.Extensions.Linq;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.Connection;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    using Error = Common.Data.Error;

    public class ConnectionService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor, Lazy<IStringLocalizer<ConnectionService>> localizer, Lazy<ILogger<ConnectionService>> logger
        , Lazy<IFileService> fileService)
        : LocalizableServiceBase<ConnectionService>(unitOfWorkProvider, httpContextAccessor, localizer, logger), IConnectionService
    {
        public async Task<ResultData<ListDataSource<FollowDto>>> GetFollowersAsync(ListRequestDto<Connection>? requestDto = null)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var query = await uow.GetRepository<Connection>().GetManyQueryable(requestDto?.Specification).FilterListAsync(requestDto?.PagingDto);

                var lst = await query.List.Select(t => new
                {
                    t.SourceUserId,
                    t.SourceUser.AvatarId,
                    t.SourceUser.FirstName,
                    t.SourceUser.LastName,
                }).ToListAsync();
                if (lst is null)
                {
                    return new(OperationResult.Succeeded) { Data = new() { TotalRecordsCount = query.TotalRecordsCount } };
                }


                List<FollowDto> result = new(lst.Count);
                for (var i = 0; i < lst.Count; i++)
                {
                    result.Add(new()
                    {
                        UserId = lst[i].SourceUserId,
                        Name = $"{lst[i].FirstName} {lst[i].LastName}",
                        AvatarUri = fileService.Value.GetStaticFileUrl(new() { FileId = lst[i].AvatarId, ContainerType = ContainerType.User, }),
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

        public async Task<ResultData<ListDataSource<FollowDto>>> GetFollowingsAsync(ListRequestDto<Connection>? requestDto = null)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var query = await uow.GetRepository<Connection>().GetManyQueryable(requestDto?.Specification).FilterListAsync(requestDto?.PagingDto);

                var lst = await query.List.Select(t => new
                {
                    t.DestinationUserId,
                    t.DestinationUser.AvatarId,
                    t.DestinationUser.FirstName,
                    t.DestinationUser.LastName,
                }).ToListAsync();
                if (lst is null)
                {
                    return new(OperationResult.Succeeded) { Data = new() { TotalRecordsCount = query.TotalRecordsCount } };
                }


                List<FollowDto> result = new(lst.Count);
                for (var i = 0; i < lst.Count; i++)
                {
                    result.Add(new()
                    {
                        UserId = lst[i].DestinationUserId,
                        Name = $"{lst[i].FirstName} {lst[i].LastName}",
                        AvatarUri = fileService.Value.GetStaticFileUrl(new() { FileId = lst[i].AvatarId, ContainerType = ContainerType.User, }),
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

        public async Task<ResultData<bool>> FollowAsync([NotNull] FollowRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<Connection>();
                ConnectionStatus[] statuses = [ConnectionStatus.Confirmed, ConnectionStatus.Requested];
                var status = await repository.GetManyQueryable(t => t.SourceUserId == requestDto.UserId && t.DestinationUserId == requestDto.ProfileId && statuses.Contains(t.Status)).Select(t => t.Status).FirstOrDefaultAsync();
                if (status == ConnectionStatus.Confirmed)
                {
                    return new(OperationResult.NotValid)
                    {
                        Errors = new[] { new Error { Message = "There is a Confirmed  Request" } }
                    };
                }

                if (status == ConnectionStatus.Requested)
                {
                    return new(OperationResult.NotValid)
                    {
                        Errors = new[] { new Error { Message = "There is a Pending Follow Request" } }
                    };
                }

                repository.Add(new()
                {
                    SourceUserId = requestDto.UserId,
                    DestinationUserId = requestDto.ProfileId,
                    SubscribeToActivityFeed = requestDto.SubscribeToActivityFeed,
                    CreationDate = DateTimeOffset.UtcNow,
                    Status = ConnectionStatus.Requested,
                });
                _ = await uow.SaveChangesAsync();

                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<bool>> UnFollowAsync([NotNull] UnFollowRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                using var trn = uow.CreateTransactionScope();

                var repository = uow.GetRepository<Connection>();
                var affectedRows = await repository.GetManyQueryable(t => t.SourceUserId == requestDto.UserId && t.DestinationUserId == requestDto.ProfileId && t.Status == ConnectionStatus.Confirmed).ExecuteUpdateAsync(t => t
                    .SetProperty(p => p.Status, ConnectionStatus.Revoked));
                if (affectedRows == 0)
                {
                    return new(OperationResult.NotValid)
                    {
                        Errors = new[] { new Error { Message = "There is no Confirmed  Request" } }
                    };
                }

                if (requestDto.TwoWayRevoke)
                {
                    _ = await repository.GetManyQueryable(t => t.SourceUserId == requestDto.ProfileId && t.DestinationUserId == requestDto.UserId && t.Status == ConnectionStatus.Confirmed).ExecuteUpdateAsync(t => t
                        .SetProperty(p => p.Status, ConnectionStatus.Revoked));
                }

                trn.Complete();

                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<bool>> ToggleSubscriptionAsync([NotNull] ToggleSubscriptionRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<Connection>();
                var affectedRows = await repository.GetManyQueryable(t => t.SourceUserId == requestDto.UserId && t.DestinationUserId == requestDto.ProfileId && t.Status == ConnectionStatus.Confirmed).ExecuteUpdateAsync(t => t
                    .SetProperty(p => p.SubscribeToActivityFeed, p => !p.SubscribeToActivityFeed));

                return affectedRows == 0
                    ? new(OperationResult.NotValid)
                    {
                        Errors = new[] { new Error { Message = "There is no Confirmed Connection" } }
                    }
                    : new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<ListDataSource<FollowRequestsResponseDto>>> GetFollowRequestsAsync([NotNull] FollowRequestsRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var query = await uow.GetRepository<Connection>().GetManyQueryable(t => t.DestinationUserId == requestDto.UserId && t.Status == ConnectionStatus.Requested).FilterListAsync(requestDto.PagingDto);

                var lst = await query.List.Select(t => new
                {
                    t.Id,
                    t.SourceUserId,
                    t.SourceUser.AvatarId,
                    t.SourceUser.FirstName,
                    t.SourceUser.LastName,
                }).ToListAsync();
                if (lst is null)
                {
                    return new(OperationResult.Succeeded) { Data = new() { TotalRecordsCount = query.TotalRecordsCount } };
                }


                List<FollowRequestsResponseDto> result = new(lst.Count);
                for (var i = 0; i < lst.Count; i++)
                {
                    result.Add(new()
                    {
                        UserId = lst[i].SourceUserId,
                        Name = $"{lst[i].FirstName} {lst[i].LastName}",
                        AvatarUri = fileService.Value.GetStaticFileUrl(new() { FileId = lst[i].AvatarId, ContainerType = ContainerType.User, }),
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

        public async Task<ResultData<bool>> ConfirmFollowRequestAsync([NotNull] ConfirmFollowRequestRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<Connection>();
                using var trn = uow.CreateTransactionScope();

                var affectedRows = await repository.GetManyQueryable(t => t.Id == requestDto.Id && t.DestinationUserId == requestDto.UserId && t.Status == ConnectionStatus.Requested)
                    .ExecuteUpdateAsync(t => t.SetProperty(p => p.Status, ConnectionStatus.Rejected));
                if (affectedRows > 0 && requestDto.TwoWay)
                {
                    var followerId = await repository.GetManyQueryable(t => t.Id == requestDto.Id).Select(t => t.SourceUserId).FirstOrDefaultAsync();
                    repository.Add(new()
                    {
                        SourceUserId = requestDto.UserId,
                        DestinationUserId = followerId,
                        Status = ConnectionStatus.Confirmed,
                        CreationDate = DateTimeOffset.UtcNow,
                    });
                }

                trn.Complete();
                return new(OperationResult.Succeeded) { Data = affectedRows > 0 };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<bool>> RejectFollowRequestAsync([NotNull] RejectFollowRequestRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var affectedRows = await uow.GetRepository<Connection>().GetManyQueryable(t => t.Id == requestDto.Id && t.DestinationUserId == requestDto.UserId && t.Status == ConnectionStatus.Requested)
                    .ExecuteUpdateAsync(t => t.SetProperty(p => p.Status, ConnectionStatus.Rejected));

                return new(OperationResult.Succeeded) { Data = affectedRows > 0 };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }
    }
}

