namespace GamaEdtech.Application.Service
{
    using System.Diagnostics.CodeAnalysis;

    using EntityFramework.Exceptions.Common;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Core.Extensions.Linq;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Data.Dto.Content;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    public class CommissionPayoutService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor
        , Lazy<IStringLocalizer<CommissionPayoutService>> localizer, Lazy<ILogger<CommissionPayoutService>> logger
        , Lazy<IApplicationSettingsService> applicationSettingsService, Lazy<ITwoFactorService> twoFactorService)
        : LocalizableServiceBase<CommissionPayoutService>(unitOfWorkProvider, httpContextAccessor, localizer, logger), ICommissionPayoutService
    {
        private const string PayoutNotFound = "Payout request not found.";

        public async Task<ResultData<CommissionBalanceDto>> GetBalanceAsync(long userId)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var earned = await uow.GetRepository<ContentOwnerCommission>().GetManyQueryable(t => t.OwnerUserId == userId)
                    .SumAsync(t => (decimal?)t.AmountUsd) ?? 0m;

                var payouts = uow.GetRepository<CommissionPayout>().GetManyQueryable(t => t.UserId == userId);
                var paidOut = await payouts.Where(t => t.Status == PayoutStatus.Paid).SumAsync(t => (decimal?)t.AmountUsd) ?? 0m;
                var open = await payouts.Where(t => t.Status == PayoutStatus.Pending || t.Status == PayoutStatus.Approved)
                    .Select(t => new { t.Id, t.AmountUsd })
                    .FirstOrDefaultAsync();
                var reserved = open?.AmountUsd ?? 0m;

                var settings = await applicationSettingsService.Value.GetApplicationSettingsAsync();

                return new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        TotalEarnedUsd = earned,
                        PaidOutUsd = paidOut,
                        ReservedUsd = reserved,
                        AvailableUsd = Math.Max(0m, Math.Floor((earned - paidOut - reserved) * 100m) / 100m),
                        PayoutThresholdUsd = settings.Data?.ContentOwnerCommissionPayoutThresholdUsd ?? 0m,
                        OpenPayoutId = open?.Id,
                    },
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<ListDataSource<CommissionPayoutDto>>> GetPayoutsAsync(ListRequestDto<CommissionPayout>? requestDto = null)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                // Newest first unless the client asks for another sort (FilterListAsync keeps this order when no SortFilter is sent).
                var result = await uow.GetRepository<CommissionPayout>().GetManyQueryable(requestDto?.Specification)
                    .OrderByDescending(t => t.Id)
                    .FilterListAsync(requestDto?.PagingDto);
                var payouts = await result.List.Select(t => new CommissionPayoutDto
                {
                    Id = t.Id,
                    UserId = t.UserId,
                    UserFirstName = t.User!.FirstName,
                    UserLastName = t.User.LastName,
                    AmountUsd = t.AmountUsd,
                    Destination = t.Destination,
                    Status = t.Status,
                    CreationDate = t.CreationDate,
                    ApprovedByUserId = t.ApprovedByUserId,
                    ApprovedByFullName = t.ApprovedBy == null ? null : t.ApprovedBy.FirstName + " " + t.ApprovedBy.LastName,
                    ApprovalDate = t.ApprovalDate,
                    PaidByUserId = t.PaidByUserId,
                    PaidByFullName = t.PaidBy == null ? null : t.PaidBy.FirstName + " " + t.PaidBy.LastName,
                    PaidDate = t.PaidDate,
                    TransferReference = t.TransferReference,
                    RejectedByUserId = t.RejectedByUserId,
                    RejectedByFullName = t.RejectedBy == null ? null : t.RejectedBy.FirstName + " " + t.RejectedBy.LastName,
                    RejectionDate = t.RejectionDate,
                    RejectionReason = t.RejectionReason,
                    CancellationDate = t.CancellationDate,
                }).ToListAsync();
                return new(OperationResult.Succeeded) { Data = new() { List = payouts, TotalRecordsCount = result.TotalRecordsCount } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<long>> RequestPayoutAsync([NotNull] RequestCommissionPayoutRequestDto requestDto)
        {
            try
            {
                var destination = requestDto.Destination?.Trim();
                if (string.IsNullOrEmpty(destination))
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = "Enter where the payout should be sent." }] };
                }

                var balanceResult = await GetBalanceAsync(requestDto.UserId);
                if (balanceResult.OperationResult is not OperationResult.Succeeded)
                {
                    return new(balanceResult.OperationResult) { Errors = balanceResult.Errors };
                }

                var balance = balanceResult.Data!;
                if (balance.OpenPayoutId.HasValue)
                {
                    return new(OperationResult.Duplicate) { Errors = [new() { Message = "You already have an open payout request." }] };
                }

                var amount = requestDto.AmountUsd ?? balance.AvailableUsd;
                if (amount <= 0 || decimal.Round(amount, 2) != amount)
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = "The amount must be positive, in whole cents." }] };
                }

                if (amount < balance.PayoutThresholdUsd)
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = $"The minimum payout is {balance.PayoutThresholdUsd:0.##} USD." }] };
                }

                if (amount > balance.AvailableUsd)
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = $"The amount is more than your available balance ({balance.AvailableUsd:0.##} USD)." }] };
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var payout = new CommissionPayout
                {
                    UserId = requestDto.UserId,
                    AmountUsd = amount,
                    Destination = destination,
                    Status = PayoutStatus.Pending,
                    CreationDate = DateTimeOffset.UtcNow,
                };
                uow.GetRepository<CommissionPayout>().Add(payout);
                _ = await uow.SaveChangesAsync();

                return new(OperationResult.Succeeded) { Data = payout.Id };
            }
            catch (UniqueConstraintException)
            {
                // A concurrent request won the "one open request per owner" index - see CommissionPayout.Configure.
                return new(OperationResult.Duplicate) { Errors = [new() { Message = "You already have an open payout request." }] };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<bool>> CancelPayoutAsync(long userId, long payoutId)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var affectedRows = await uow.GetRepository<CommissionPayout>()
                    .GetManyQueryable(t => t.Id == payoutId && t.UserId == userId && t.Status == PayoutStatus.Pending)
                    .ExecuteUpdateAsync(t => t
                        .SetProperty(p => p.Status, PayoutStatus.Cancelled)
                        .SetProperty(p => p.CancellationDate, DateTimeOffset.UtcNow));
                return affectedRows == 0
                    ? new(OperationResult.NotValid) { Errors = [new() { Message = "Only your own pending payout request can be cancelled." }] }
                    : new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<bool>> ApprovePayoutAsync([NotNull] ReviewCommissionPayoutRequestDto requestDto)
        {
            try
            {
                var check = await CheckReviewAsync(requestDto);
                if (check.OperationResult is not OperationResult.Succeeded)
                {
                    return check;
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var now = DateTimeOffset.UtcNow;
                var affectedRows = await uow.GetRepository<CommissionPayout>()
                    .GetManyQueryable(t => t.Id == requestDto.PayoutId && t.Status == PayoutStatus.Pending)
                    .ExecuteUpdateAsync(t => t
                        .SetProperty(p => p.Status, PayoutStatus.Approved)
                        .SetProperty(p => p.ApprovedByUserId, requestDto.AdminUserId)
                        .SetProperty(p => p.ApprovalDate, now));
                if (affectedRows == 0)
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = "Only a pending payout request can be approved." }] };
                }

                LogDecision("approved", requestDto);
                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<bool>> RejectPayoutAsync([NotNull] ReviewCommissionPayoutRequestDto requestDto)
        {
            try
            {
                var reason = requestDto.RejectionReason?.Trim();
                if (string.IsNullOrEmpty(reason))
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = "Enter a rejection reason." }] };
                }

                var check = await CheckReviewAsync(requestDto);
                if (check.OperationResult is not OperationResult.Succeeded)
                {
                    return check;
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var now = DateTimeOffset.UtcNow;
                var affectedRows = await uow.GetRepository<CommissionPayout>()
                    .GetManyQueryable(t => t.Id == requestDto.PayoutId && (t.Status == PayoutStatus.Pending || t.Status == PayoutStatus.Approved))
                    .ExecuteUpdateAsync(t => t
                        .SetProperty(p => p.Status, PayoutStatus.Rejected)
                        .SetProperty(p => p.RejectedByUserId, requestDto.AdminUserId)
                        .SetProperty(p => p.RejectionDate, now)
                        .SetProperty(p => p.RejectionReason, reason));
                if (affectedRows == 0)
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = "Only a pending or approved payout request can be rejected." }] };
                }

                LogDecision("rejected", requestDto);
                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<bool>> MarkPayoutPaidAsync([NotNull] ReviewCommissionPayoutRequestDto requestDto)
        {
            try
            {
                var transferReference = requestDto.TransferReference?.Trim();
                if (string.IsNullOrEmpty(transferReference))
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = "Enter the transfer reference." }] };
                }

                var check = await CheckReviewAsync(requestDto);
                if (check.OperationResult is not OperationResult.Succeeded)
                {
                    return check;
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var now = DateTimeOffset.UtcNow;
                var affectedRows = await uow.GetRepository<CommissionPayout>()
                    .GetManyQueryable(t => t.Id == requestDto.PayoutId && t.Status == PayoutStatus.Approved)
                    .ExecuteUpdateAsync(t => t
                        .SetProperty(p => p.Status, PayoutStatus.Paid)
                        .SetProperty(p => p.PaidByUserId, requestDto.AdminUserId)
                        .SetProperty(p => p.PaidDate, now)
                        .SetProperty(p => p.TransferReference, transferReference));
                if (affectedRows == 0)
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = "Only an approved payout request can be marked as paid." }] };
                }

                LogDecision("marked paid", requestDto);
                return new(OperationResult.Succeeded) { Data = true };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        /// <summary>
        /// Shared gate for every admin decision: the payout exists, it isn't the admin's own, and the admin's 2FA code is
        /// valid. The status transition itself is checked atomically by the caller's conditional update, so two admins
        /// deciding at once can't both win.
        /// </summary>
        private async Task<ResultData<bool>> CheckReviewAsync(ReviewCommissionPayoutRequestDto requestDto)
        {
            var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
            var ownerId = await uow.GetRepository<CommissionPayout>().GetManyQueryable(t => t.Id == requestDto.PayoutId)
                .Select(t => (long?)t.UserId)
                .FirstOrDefaultAsync();
            return ownerId switch
            {
                null => new(OperationResult.NotFound) { Errors = [new() { Message = PayoutNotFound }] },
                var id when id == requestDto.AdminUserId => new(OperationResult.NotValid) { Errors = [new() { Message = "You can't decide on your own payout request." }] },
                _ => await twoFactorService.Value.VerifyCodeAsync(requestDto.AdminUserId, requestDto.TwoFactorCode),
            };
        }

        private void LogDecision(string decision, ReviewCommissionPayoutRequestDto requestDto)
        {
            if (Logger.Value.IsEnabled(LogLevel.Information))
            {
                Logger.Value.LogInformation("Commission payout {PayoutId} {Decision} by admin UserId {AdminUserId}", requestDto.PayoutId, decision, requestDto.AdminUserId);
            }
        }
    }
}
