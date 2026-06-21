namespace GamaEdtech.Application.Service
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Text.Json;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Core.Extensions.Linq;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.Contribution;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Domain.Specification;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    public class ContributionService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor, Lazy<IStringLocalizer<ContributionService>> localizer
        , Lazy<ILogger<ContributionService>> logger, Lazy<IApplicationSettingsService> applicationSettingsService, Lazy<ITransactionService> transactionService)
        : LocalizableServiceBase<ContributionService>(unitOfWorkProvider, httpContextAccessor, localizer, logger), IContributionService
    {
        public async Task<ResultData<ListDataSource<ContributionsDto<T>>>> GetContributionsAsync<T>(ListRequestDto<Contribution>? requestDto = null, bool includeData = false)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var query = await uow.GetRepository<Contribution>().GetManyQueryable(requestDto?.Specification).FilterListAsync(requestDto?.PagingDto);
                var lst = await query.List.Select(t => new
                {
                    t.Id,
                    t.Comment,
                    t.CategoryType,
                    t.IdentifierId,
                    t.Status,
                    CreationUser = t.CreationUser.FirstName + " " + t.CreationUser.LastName,
                    t.CreationDate,
                    t.LastModifyDate,
                    Data = includeData ? t.Data : null,
                }).ToListAsync();

                var result = lst.Select(t => new ContributionsDto<T>
                {
                    Id = t.Id,
                    CategoryType = t.CategoryType,
                    Comment = t.Comment,
                    CreationDate = t.CreationDate,
                    CreationUser = t.CreationUser,
                    IdentifierId = t.IdentifierId,
                    LastModifyDate = t.LastModifyDate,
                    Status = t.Status,
                    Data = string.IsNullOrEmpty(t.Data) ? default : JsonSerializer.Deserialize<T>(t.Data),
                });

                return new(OperationResult.Succeeded) { Data = new() { List = result, TotalRecordsCount = query.TotalRecordsCount } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<ContributionDto<T>>> GetContributionAsync<T>([NotNull] ISpecification<Contribution> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var dto = await uow.GetRepository<Contribution>().GetManyQueryable(specification).Select(t => new
                {
                    t.Id,
                    t.Comment,
                    t.Data,
                    t.IdentifierId,
                    t.CategoryType,
                }).FirstOrDefaultAsync();
                if (dto is null)
                {
                    return new(OperationResult.NotFound)
                    {
                        Errors = [new() { Message = Localizer.Value["ContributionNotFound"] },],
                    };
                }

                ContributionDto<T> contribution = new()
                {
                    Id = dto.Id,
                    Comment = dto.Comment,
                    CategoryType = dto.CategoryType,
                    IdentifierId = dto.IdentifierId,
                    Data = dto.Data is null ? default : JsonSerializer.Deserialize<T>(dto.Data),
                };

                return new(OperationResult.Succeeded) { Data = contribution };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<T?>> GetContributionDataAsync<T>([NotNull] ISpecification<Contribution> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var data = await uow.GetRepository<Contribution>().GetManyQueryable(specification)
                    .Select(t => t.Data).FirstOrDefaultAsync();

                return new(OperationResult.Succeeded) { Data = string.IsNullOrEmpty(data) ? default : JsonSerializer.Deserialize<T>(data) };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<long>> ManageContributionAsync<T>([NotNull] ManageContributionRequestDto<T> requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<Contribution>();
                Contribution? contribution = null;

                if (requestDto.Id.HasValue)
                {
                    contribution = await repository.GetAsync(requestDto.Id.Value);
                    if (contribution is null)
                    {
                        return new(OperationResult.NotFound)
                        {
                            Errors = [new() { Message = Localizer.Value["ContributionNotFound"] },],
                        };
                    }

                    contribution.Comment = requestDto.Comment;
                    contribution.CategoryType = requestDto.CategoryType;
                    contribution.IdentifierId = requestDto.IdentifierId;
                    contribution.Status = requestDto.Status;
                    if (string.IsNullOrEmpty(contribution.Data))
                    {
                        contribution.Data = JsonSerializer.Serialize(requestDto.Data);
                    }
                    else
                    {
                        var dto = JsonSerializer.Deserialize<T>(contribution.Data);
                        var properties = typeof(T).GetProperties();
                        for (var i = 0; i < properties.Length; i++)
                        {
                            var property = properties[i];
                            if (!property.CanWrite)
                            {
                                continue;
                            }

                            var value = property.GetValue(requestDto.Data);
                            if (value is not null)
                            {
                                property.SetValue(dto, value);
                            }
                        }
                        contribution.Data = JsonSerializer.Serialize(dto);
                    }

                    _ = repository.Update(contribution);
                }
                else
                {
                    contribution = new Contribution
                    {
                        Comment = requestDto.Comment,
                        CategoryType = requestDto.CategoryType,
                        Data = JsonSerializer.Serialize(requestDto.Data),
                        IdentifierId = requestDto.IdentifierId,
                        Status = requestDto.Status,
                    };
                    repository.Add(contribution);
                }

                _ = await uow.SaveChangesAsync();

                return new(OperationResult.Succeeded) { Data = contribution.Id };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        public async Task<ResultData<bool>> ExistsContributionAsync([NotNull] ISpecification<Contribution> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<Contribution>();

                return new(OperationResult.Succeeded) { Data = await repository.AnyAsync(specification) };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        public async Task<ResultData<ContributionDto<T>>> ConfirmContributionAsync<T>([NotNull] ConfirmContributionRequestDto<Contribution> requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<Contribution>();
                var contribution = await repository.GetManyQueryable(requestDto.Specification.And(new StatusEqualsSpecification<Contribution>(Status.Review))).Select(t => new
                {
                    t.Id,
                    t.CategoryType,
                    t.Data,
                    t.Comment,
                    t.CreationDate,
                    t.IdentifierId,
                    t.CreationUserId,
                    t.CreationUser.Email,
                    FullName = t.CreationUser.FirstName + " " + t.CreationUser.LastName,
                }).FirstOrDefaultAsync();
                if (contribution is null)
                {
                    return new(OperationResult.NotFound)
                    {
                        Errors = [new() { Message = Localizer.Value["ContributionNotFound"] },],
                    };
                }

                _ = await repository.GetManyQueryable(t => t.Id == contribution.Id).ExecuteUpdateAsync(t => t.SetProperty(p => p.Status, Status.Confirmed));

                var points = await applicationSettingsService.Value.GetSettingAsync<long>(contribution.CategoryType.ApplicationSettingsName);
                if (points.Data > 0)
                {
                    _ = await transactionService.Value.IncreaseBalanceAsync(new()
                    {
                        Description = $"Successful Contribution - {contribution.CategoryType.Name}",
                        Points = points.Data,
                        IdentifierId = contribution.Id,
                        UserId = contribution.CreationUserId,
                        TransactionType = TransactionType.SuccessfulContribution,
                    });
                }

                return new(OperationResult.Succeeded)
                {
                    Data = new ContributionDto<T>
                    {
                        Id = contribution.Id,
                        Data = string.IsNullOrEmpty(contribution.Data) ? default : JsonSerializer.Deserialize<T>(contribution.Data),
                        Comment = contribution.Comment,
                        CreationUserId = contribution.CreationUserId,
                        Email = contribution.Email!,
                        FullName = contribution.FullName,
                        CreationDate = contribution.CreationDate,
                        CategoryType = contribution.CategoryType,
                        IdentifierId = contribution.IdentifierId,
                    }
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        public async Task<ResultData<ContributionDto<T>>> RejectContributionAsync<T>([NotNull] RejectContributionRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<Contribution>();

                var affectedRows = await repository.GetManyQueryable(t => t.Id == requestDto.Id && t.Status == Status.Review).ExecuteUpdateAsync(t => t
                    .SetProperty(p => p.Status, Status.Rejected)
                    .SetProperty(p => p.Comment, requestDto.Comment)
                    .SetProperty(p => p.LastModifyUserId, requestDto.UserId)
                    .SetProperty(p => p.LastModifyDate, DateTimeOffset.UtcNow));
                if (affectedRows == 0)
                {
                    return new(OperationResult.NotFound)
                    {
                        Errors = [new() { Message = Localizer.Value["ContributionNotFound"] },],
                    };
                }

                var contribution = await repository.GetManyQueryable(t => t.Id == requestDto.Id).Select(t => new
                {
                    t.Id,
                    t.CategoryType,
                    t.Data,
                    t.Comment,
                    t.CreationDate,
                    t.IdentifierId,
                    t.CreationUserId,
                    t.CreationUser.Email,
                    FullName = t.CreationUser.FirstName + " " + t.CreationUser.LastName,
                }).FirstOrDefaultAsync();

                return new(OperationResult.Succeeded)
                {
                    Data = new ContributionDto<T>
                    {
                        Id = contribution!.Id,
                        Data = string.IsNullOrEmpty(contribution.Data) ? default : JsonSerializer.Deserialize<T>(contribution.Data),
                        Comment = contribution.Comment,
                        CreationUserId = contribution.CreationUserId,
                        Email = contribution.Email!,
                        FullName = contribution.FullName,
                        CreationDate = contribution.CreationDate,
                        CategoryType = contribution.CategoryType,
                        IdentifierId = contribution.IdentifierId,
                    }
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        public async Task<ResultData<bool>> DeleteContributionAsync([NotNull] ISpecification<Contribution> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<Contribution>();
                var userId = HttpContextAccessor.Value.HttpContext?.User.UserId();

                var contribution = await repository.GetAsync(specification.And(new StatusEqualsSpecification<Contribution>(Status.Confirmed)));
                if (contribution is null)
                {
                    return new(OperationResult.Failed) { Errors = [new() { Message = "Invalid Request", }] };
                }

                contribution.Status = Status.Deleted;
                contribution.LastModifyUserId = userId;
                contribution.LastModifyDate = DateTimeOffset.UtcNow;
                _ = repository.Update(contribution);
                _ = await uow.SaveChangesAsync();

                var transactionSpecification = new IdentifierIdEqualsSpecification<Transaction>(contribution.Id);
                var previousTransaction = await transactionService.Value.GetTransactionAsync(transactionSpecification);
                if (previousTransaction.Data is null)
                {
                    return new(OperationResult.Succeeded) { Data = true };
                }

                _ = await transactionService.Value.DecreaseBalanceAsync(new()
                {
                    Description = "Delete Contribution",
                    IdentifierId = previousTransaction.Data.Id,
                    UserId = previousTransaction.Data.UserId,
                    Points = previousTransaction.Data.Points,
                    TransactionType = TransactionType.DeleteContribution,
                });

                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        public async Task<ResultData<bool>> IsCreatorOfContributionAsync(long contributionId, long userId)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<Contribution>();

                var exists = await repository.AnyAsync(t => t.Id == contributionId && t.CreationUserId == userId);

                return new(OperationResult.Succeeded) { Data = exists };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        public async Task<ResultData<bool>> UpdateIdentifierIdAsync(long contributionId, long identifierId)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<Contribution>();

                var affectedRows = await repository.GetManyQueryable(t => t.Id == contributionId)
                    .ExecuteUpdateAsync(t => t.SetProperty(p => p.IdentifierId, identifierId));

                return new(OperationResult.Succeeded) { Data = affectedRows > 0 };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        public async Task<ResultData<long?>> GetIdentifierIdAsync([NotNull] ISpecification<Contribution> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var identifierId = await uow.GetRepository<Contribution>().GetManyQueryable(specification).Select(t => t.IdentifierId).FirstOrDefaultAsync();

                return new(OperationResult.Succeeded) { Data = identifierId };
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
    }
}
