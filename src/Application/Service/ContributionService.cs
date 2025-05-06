namespace GamaEdtech.Application.Service
{
    using System;
    using System.Diagnostics.CodeAnalysis;

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
        public async Task<ResultData<ListDataSource<ContributionsDto>>> GetContributionsAsync(ListRequestDto<Contribution>? requestDto = null, bool includeData = false)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var result = await uow.GetRepository<Contribution>().GetManyQueryable(requestDto?.Specification).FilterListAsync(requestDto?.PagingDto);
                var users = await result.List.Select(t => new ContributionsDto
                {
                    Id = t.Id,
                    Comment = t.Comment,
                    CategoryType = t.CategoryType,
                    IdentifierId = t.IdentifierId,
                    Status = t.Status,
                    CreationUser = t.CreationUser!.FirstName + " " + t.CreationUser.LastName,
                    CreationDate = t.CreationDate,
                    LastModifyDate = t.LastModifyDate,
                    Data = includeData ? t.Data : null,
                }).ToListAsync();
                return new(OperationResult.Succeeded) { Data = new() { List = users, TotalRecordsCount = result.TotalRecordsCount } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<ContributionDto>> GetContributionAsync([NotNull] ISpecification<Contribution> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var contribution = await uow.GetRepository<Contribution>().GetManyQueryable(specification).Select(t => new ContributionDto
                {
                    Id = t.Id,
                    Comment = t.Comment,
                    Data = t.Data,
                    IdentifierId = t.IdentifierId,
                    CategoryType = t.CategoryType,
                }).FirstOrDefaultAsync();

                return contribution is null
                    ? new(OperationResult.NotFound)
                    {
                        Errors = [new() { Message = Localizer.Value["ContributionNotFound"] },],
                    }
                    : new(OperationResult.Succeeded) { Data = contribution };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<long>> ManageContributionAsync([NotNull] ManageContributionRequestDto requestDto)
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
                    contribution.Data = requestDto.Data;
                    contribution.IdentifierId = requestDto.IdentifierId;
                    contribution.Status = requestDto.Status;

                    _ = repository.Update(contribution);
                }
                else
                {
                    contribution = new Contribution
                    {
                        Comment = requestDto.Comment,
                        CategoryType = requestDto.CategoryType,
                        Data = requestDto.Data,
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

        public async Task<ResultData<ContributionDto>> ConfirmContributionAsync([NotNull] ISpecification<Contribution> specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<Contribution>();
                var contribution = await repository.GetAsync(specification);
                if (contribution is null)
                {
                    return new(OperationResult.NotFound)
                    {
                        Errors = [new() { Message = Localizer.Value["ContributionNotFound"] },],
                    };
                }

                contribution.Status = Status.Confirmed;
                _ = repository.Update(contribution);
                _ = await uow.SaveChangesAsync();

                var points = await applicationSettingsService.Value.GetSettingAsync<int>(contribution.CategoryType.ApplicationSettingsName);
                if (points.Data > 0)
                {
                    _ = await transactionService.Value.IncreaseBalanceAsync(new()
                    {
                        Description = "Successful Contribution",
                        Points = points.Data,
                        IdentifierId = contribution.Id,
                        UserId = contribution.CreationUserId,
                    });
                }

                return new(OperationResult.Succeeded)
                {
                    Data = new ContributionDto
                    {
                        Id = contribution.Id,
                        Data = contribution.Data,
                        Comment = contribution.Comment,
                        CreationUserId = contribution.CreationUserId,
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

        public async Task<ResultData<bool>> RejectContributionAsync([NotNull] RejectContributionRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<Contribution>();
                var userId = HttpContextAccessor.Value.HttpContext?.User.UserId();
                var affectedRows = await repository.GetManyQueryable(t => t.Id == requestDto.Id).ExecuteUpdateAsync(t => t
                    .SetProperty(p => p.Status, Status.Rejected)
                    .SetProperty(p => p.Comment, requestDto.Comment)
                    .SetProperty(p => p.LastModifyUserId, userId)
                    .SetProperty(p => p.LastModifyDate, DateTimeOffset.UtcNow));

                return new(OperationResult.Succeeded) { Data = affectedRows > 0 };
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
                });

                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        public async Task<ResultData<bool>> IsCreatorOfContributionAsync(long contributionId, int userId)
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
    }
}
