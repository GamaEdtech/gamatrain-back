namespace GamaEdtech.Application.Service
{
    using System.Diagnostics.CodeAnalysis;
    using System.Globalization;
    using System.Net;

    using EntityFramework.Exceptions.Common;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Core.Extensions.Linq;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Common.Service.Factory;
    using GamaEdtech.Data.Dto.ApplicationSettings;
    using GamaEdtech.Data.Dto.Content;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    public class CommissionPayoutService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor
        , Lazy<IStringLocalizer<CommissionPayoutService>> localizer, Lazy<ILogger<CommissionPayoutService>> logger
        , Lazy<IApplicationSettingsService> applicationSettingsService, Lazy<ITwoFactorService> twoFactorService
        , Lazy<IEmailService> emailService, Lazy<IGenericFactory<IPayoutProvider, PayoutMethod>> payoutProviderFactory
        , Lazy<IConfiguration> configuration)
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

                // Hard floor: the admin form rejects a minimum below $100, and a lower stored value (saved before that rule,
                // or edited in the database) is raised to it here, so the minimum can never be lowered or switched off.
                var settings = await applicationSettingsService.Value.GetApplicationSettingsAsync();
                var threshold = Math.Max(settings.Data?.ContentOwnerCommissionPayoutThresholdUsd ?? 0m, ApplicationSettingsDto.MinContentOwnerCommissionPayoutThresholdUsd);

                return new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        TotalEarnedUsd = earned,
                        PaidOutUsd = paidOut,
                        ReservedUsd = reserved,
                        AvailableUsd = Math.Max(0m, Math.Floor((earned - paidOut - reserved) * 100m) / 100m),
                        PayoutThresholdUsd = threshold,
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
                    Method = t.Method,
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
                string? destination;
                if (requestDto.Method == PayoutMethod.StripeConnect)
                {
                    // Re-read from Stripe: the owner may have just finished (or lost) payout eligibility.
                    var account = await GetPayoutAccountAsync(requestDto.UserId);
                    if (account.Data?.PayoutsEnabled != true)
                    {
                        return new(OperationResult.NotValid) { Errors = [new() { Message = "Finish setting up Stripe payouts before requesting a Stripe payout." }] };
                    }

                    destination = $"Stripe account {await GetExternalAccountIdAsync(requestDto.UserId)}";
                }
                else
                {
                    destination = requestDto.Destination?.Trim();
                    if (string.IsNullOrEmpty(destination))
                    {
                        return new(OperationResult.NotValid) { Errors = [new() { Message = "Enter where the payout should be sent." }] };
                    }
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
                    Method = requestDto.Method,
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

        public async Task<ResultData<PayoutStatus>> ApprovePayoutAsync([NotNull] ReviewCommissionPayoutRequestDto requestDto)
        {
            try
            {
                var (check, target) = await CheckReviewAsync(requestDto);
                if (check.OperationResult is not OperationResult.Succeeded)
                {
                    return new(check.OperationResult) { Errors = check.Errors };
                }

                // Claim it first (Pending -> Approved, one winner): for Stripe this is also what keeps two admins from
                // both sending the transfer.
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
                if (target!.Method != PayoutMethod.StripeConnect)
                {
                    return new(OperationResult.Succeeded) { Data = PayoutStatus.Approved };
                }

                var transfer = await SendStripeTransferAsync(requestDto.PayoutId, target, requestDto.AdminUserId);
                if (transfer.OperationResult is OperationResult.Succeeded)
                {
                    return new(OperationResult.Succeeded) { Data = PayoutStatus.Paid };
                }

                // Stripe refused (e.g. not enough platform balance): back to Pending so it can be approved again later.
                // A retry is safe even if the transfer did go through: SendStripeTransferAsync finds it instead of
                // sending another.
                _ = await uow.GetRepository<CommissionPayout>()
                    .GetManyQueryable(t => t.Id == requestDto.PayoutId && t.Status == PayoutStatus.Approved)
                    .ExecuteUpdateAsync(t => t
                        .SetProperty(p => p.Status, PayoutStatus.Pending)
                        .SetProperty(p => p.ApprovedByUserId, (long?)null)
                        .SetProperty(p => p.ApprovalDate, (DateTimeOffset?)null));
                return new(OperationResult.Failed) { Errors = transfer.Errors };
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

                var (check, _) = await CheckReviewAsync(requestDto);
                if (check.OperationResult is not OperationResult.Succeeded)
                {
                    return check;
                }

                // An Approved Stripe payout may already have been transferred (see ApprovePayoutAsync), so it can't be
                // rejected - finish it with mark-paid instead.
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var now = DateTimeOffset.UtcNow;
                var affectedRows = await uow.GetRepository<CommissionPayout>()
                    .GetManyQueryable(t => t.Id == requestDto.PayoutId
                        && (t.Status == PayoutStatus.Pending || (t.Status == PayoutStatus.Approved && t.Method == PayoutMethod.Manual)))
                    .ExecuteUpdateAsync(t => t
                        .SetProperty(p => p.Status, PayoutStatus.Rejected)
                        .SetProperty(p => p.RejectedByUserId, requestDto.AdminUserId)
                        .SetProperty(p => p.RejectionDate, now)
                        .SetProperty(p => p.RejectionReason, reason));
                if (affectedRows == 0)
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = "Only a pending payout request (or an approved manual one) can be rejected." }] };
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
                var (check, target) = await CheckReviewAsync(requestDto);
                if (check.OperationResult is not OperationResult.Succeeded)
                {
                    return check;
                }

                if (target!.Method == PayoutMethod.StripeConnect)
                {
                    // Only reachable when an approval stopped between claiming the payout and recording the transfer:
                    // finish it - sends the transfer, or finds the one already sent.
                    var stuck = await UnitOfWorkProvider.Value.CreateUnitOfWork().GetRepository<CommissionPayout>()
                        .AnyAsync(t => t.Id == requestDto.PayoutId && t.Status == PayoutStatus.Approved);
                    if (!stuck)
                    {
                        return new(OperationResult.NotValid) { Errors = [new() { Message = "A Stripe payout is paid by approving it." }] };
                    }

                    var transfer = await SendStripeTransferAsync(requestDto.PayoutId, target, requestDto.AdminUserId);
                    return transfer.OperationResult is OperationResult.Succeeded
                        ? new(OperationResult.Succeeded) { Data = true }
                        : new(OperationResult.Failed) { Errors = transfer.Errors };
                }

                var transferReference = requestDto.TransferReference?.Trim();
                if (string.IsNullOrEmpty(transferReference))
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = "Enter the transfer reference." }] };
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

        public async Task<ResultData<PayoutAccountDto>> GetPayoutAccountAsync(long userId)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var account = await uow.GetRepository<UserPayoutAccount>()
                    .GetManyQueryable(t => t.UserId == userId && t.Method == PayoutMethod.StripeConnect)
                    .Select(t => new { t.Id, t.ExternalAccountId, t.Country, t.PayoutsEnabled })
                    .FirstOrDefaultAsync();
                if (account is null)
                {
                    return new(OperationResult.Succeeded) { Data = new() { HasAccount = false } };
                }

                var status = await payoutProviderFactory.Value.GetProvider(PayoutMethod.StripeConnect)!.GetAccountStatusAsync(account.ExternalAccountId!);
                if (status.OperationResult is not OperationResult.Succeeded)
                {
                    return new(status.OperationResult) { Errors = status.Errors };
                }

                if (status.Data!.PayoutsEnabled != account.PayoutsEnabled)
                {
                    _ = await uow.GetRepository<UserPayoutAccount>().GetManyQueryable(t => t.Id == account.Id)
                        .ExecuteUpdateAsync(t => t.SetProperty(p => p.PayoutsEnabled, status.Data.PayoutsEnabled));
                }

                return new(OperationResult.Succeeded)
                {
                    Data = new()
                    {
                        HasAccount = true,
                        Country = account.Country,
                        DetailsSubmitted = status.Data.DetailsSubmitted,
                        PayoutsEnabled = status.Data.PayoutsEnabled,
                    },
                };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task<ResultData<string>> CreatePayoutOnboardingLinkAsync([NotNull] PayoutOnboardingRequestDto requestDto)
        {
            try
            {
                if (!Uri.TryCreate(requestDto.ReturnUrl, UriKind.Absolute, out var returnUrl) || !IsAllowedReturnUrl(returnUrl))
                {
                    return new(OperationResult.NotValid) { Errors = [new() { Message = "The return address is not allowed." }] };
                }

                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var provider = payoutProviderFactory.Value.GetProvider(PayoutMethod.StripeConnect)!;
                var accountId = await GetExternalAccountIdAsync(requestDto.UserId);
                if (accountId is null)
                {
                    var country = requestDto.Country?.Trim().ToUpperInvariant();
                    if (country is null || country.Length != 2 || !country.All(char.IsAsciiLetterUpper))
                    {
                        return new(OperationResult.NotValid) { Errors = [new() { Message = "Choose the country of your bank account." }] };
                    }

                    var email = await uow.GetRepository<Domain.Entity.Identity.ApplicationUser>().GetManyQueryable(t => t.Id == requestDto.UserId)
                        .Select(t => t.Email).FirstOrDefaultAsync();
                    var created = await provider.CreateAccountAsync(new() { UserId = requestDto.UserId, Email = email, Country = country });
                    if (created.OperationResult is not OperationResult.Succeeded)
                    {
                        return new(created.OperationResult) { Errors = created.Errors };
                    }

                    try
                    {
                        uow.GetRepository<UserPayoutAccount>().Add(new()
                        {
                            UserId = requestDto.UserId,
                            Method = PayoutMethod.StripeConnect,
                            ExternalAccountId = created.Data,
                            Country = country,
                            PayoutsEnabled = false,
                            CreationDate = DateTimeOffset.UtcNow,
                        });
                        _ = await uow.SaveChangesAsync();
                    }
                    catch (UniqueConstraintException)
                    {
                        // A concurrent request stored it first; Stripe's idempotency key returned the same account to both.
                    }

                    accountId = await GetExternalAccountIdAsync(requestDto.UserId);
                }

                var link = await provider.CreateOnboardingLinkAsync(accountId!, WithQuery(returnUrl, "stripe", "return"), WithQuery(returnUrl, "stripe", "refresh"));
                return link.OperationResult is OperationResult.Succeeded
                    ? new(OperationResult.Succeeded) { Data = link.Data }
                    : new(link.OperationResult) { Errors = link.Errors };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message }] };
            }
        }

        public async Task SendPayoutRequestedEmailAsync(long payoutId)
        {
            var template = (await applicationSettingsService.Value.GetSettingAsync<string?>(nameof(ApplicationSettingsDto.CommissionPayoutRequestedEmailTemplate))).Data;
            await SendPayoutEmailAsync(payoutId, template, "Gamatrain - Payout Request Received", paid: false);
        }

        public async Task SendPayoutPaidEmailAsync(long payoutId)
        {
            var template = (await applicationSettingsService.Value.GetSettingAsync<string?>(nameof(ApplicationSettingsDto.CommissionPayoutPaidEmailTemplate))).Data;
            await SendPayoutEmailAsync(payoutId, template, "Gamatrain - Payout Sent", paid: true);
        }

        /// <summary>Best-effort, run as a background job: the request/decision already succeeded and is never undone by a failed email.</summary>
        private async Task SendPayoutEmailAsync(long payoutId, string? template, string subject, bool paid)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var payout = await uow.GetRepository<CommissionPayout>().GetManyQueryable(t => t.Id == payoutId).Select(t => new
                {
                    t.User!.Email,
                    t.User.FirstName,
                    t.User.LastName,
                    t.AmountUsd,
                    t.CreationDate,
                    t.PaidDate,
                    t.TransferReference,
                }).FirstOrDefaultAsync();
                if (payout is null || string.IsNullOrEmpty(payout.Email) || string.IsNullOrEmpty(template))
                {
                    return;
                }

                var name = $"{payout.FirstName} {payout.LastName}".Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = "Dear User";
                }

                var date = paid ? payout.PaidDate : payout.CreationDate;
                var body = template
                    .Replace("[RECEIVER_NAME]", WebUtility.HtmlEncode(name), StringComparison.OrdinalIgnoreCase)
                    .Replace("[PAYOUT_ID]", payoutId.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
                    .Replace("[AMOUNT]", payout.AmountUsd.ToString("0.00", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
                    .Replace("[DATE]", date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
                    .Replace("[TRANSFER_REFERENCE]", WebUtility.HtmlEncode(payout.TransferReference ?? string.Empty), StringComparison.OrdinalIgnoreCase);

                _ = await emailService.Value.SendEmailAsync(new()
                {
                    Subject = subject,
                    Body = body,
                    EmailAddresses = [payout.Email],
                    From = emailService.Value.GetNoReplyEmail(),
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
            }
        }

        /// <summary>
        /// Shared gate for every admin decision: the payout exists, it isn't the admin's own, and the admin's 2FA code is
        /// valid. The status transition itself is checked atomically by the caller's conditional update, so two admins
        /// deciding at once can't both win.
        /// </summary>
        private async Task<(ResultData<bool> Check, PayoutTarget? Target)> CheckReviewAsync(ReviewCommissionPayoutRequestDto requestDto)
        {
            var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
            var target = await uow.GetRepository<CommissionPayout>().GetManyQueryable(t => t.Id == requestDto.PayoutId)
                .Select(t => new PayoutTarget(t.UserId, t.Method, t.AmountUsd))
                .FirstOrDefaultAsync();

            var check = target switch
            {
                null => new ResultData<bool>(OperationResult.NotFound) { Errors = [new() { Message = PayoutNotFound }] },
                var t when t.UserId == requestDto.AdminUserId => new ResultData<bool>(OperationResult.NotValid) { Errors = [new() { Message = "You can't decide on your own payout request." }] },
                _ => await twoFactorService.Value.VerifyCodeAsync(requestDto.AdminUserId, requestDto.TwoFactorCode),
            };
            return (check, target);
        }

        /// <summary>
        /// Approved -> Paid for a StripeConnect payout: sends the Stripe Transfer (or finds the one already sent for this
        /// payout) and records it. The payout must already be claimed as Approved by the caller.
        /// </summary>
        private async Task<ResultData<bool>> SendStripeTransferAsync(long payoutId, PayoutTarget target, long adminUserId)
        {
            var accountId = await GetExternalAccountIdAsync(target.UserId);
            if (accountId is null)
            {
                return new(OperationResult.NotValid) { Errors = [new() { Message = "The owner has no Stripe payout account." }] };
            }

            var transfer = await payoutProviderFactory.Value.GetProvider(PayoutMethod.StripeConnect)!.TransferAsync(new()
            {
                PayoutId = payoutId,
                ExternalAccountId = accountId,
                AmountUsd = target.AmountUsd,
                IdempotencyKey = $"commission-payout-{payoutId.ToString(CultureInfo.InvariantCulture)}",
            });
            if (transfer.OperationResult is not OperationResult.Succeeded)
            {
                return new(transfer.OperationResult) { Errors = transfer.Errors };
            }

            var now = DateTimeOffset.UtcNow;
            _ = await UnitOfWorkProvider.Value.CreateUnitOfWork().GetRepository<CommissionPayout>()
                .GetManyQueryable(t => t.Id == payoutId && t.Status == PayoutStatus.Approved)
                .ExecuteUpdateAsync(t => t
                    .SetProperty(p => p.Status, PayoutStatus.Paid)
                    .SetProperty(p => p.PaidByUserId, adminUserId)
                    .SetProperty(p => p.PaidDate, now)
                    .SetProperty(p => p.TransferReference, transfer.Data));
            if (Logger.Value.IsEnabled(LogLevel.Information))
            {
                Logger.Value.LogInformation("Commission payout {PayoutId} sent through Stripe ({TransferId}) by admin UserId {AdminUserId}", payoutId, transfer.Data, adminUserId);
            }

            return new(OperationResult.Succeeded) { Data = true };
        }

        private async Task<string?> GetExternalAccountIdAsync(long userId) =>
            await UnitOfWorkProvider.Value.CreateUnitOfWork().GetRepository<UserPayoutAccount>()
                .GetManyQueryable(t => t.UserId == userId && t.Method == PayoutMethod.StripeConnect)
                .Select(t => t.ExternalAccountId)
                .FirstOrDefaultAsync();

        /// <summary>Stripe redirects the owner here after onboarding, so only our own frontends (the CORS allow-list) are accepted.</summary>
        private bool IsAllowedReturnUrl(Uri url) =>
            (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp)
            && configuration.Value.GetSection("CorsUrls").GetChildren()
                .Select(t => Uri.TryCreate(t.Value, UriKind.Absolute, out var allowed) ? allowed : null)
                .Any(t => t is not null && string.Equals(t.GetLeftPart(UriPartial.Authority), url.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase));

        private static Uri WithQuery(Uri url, string key, string value)
        {
            var builder = new UriBuilder(url);
            var query = builder.Query.TrimStart('?');
            builder.Query = (string.IsNullOrEmpty(query) ? string.Empty : query + "&") + $"{key}={value}";
            return builder.Uri;
        }

        private void LogDecision(string decision, ReviewCommissionPayoutRequestDto requestDto)
        {
            if (Logger.Value.IsEnabled(LogLevel.Information))
            {
                Logger.Value.LogInformation("Commission payout {PayoutId} {Decision} by admin UserId {AdminUserId}", requestDto.PayoutId, decision, requestDto.AdminUserId);
            }
        }

        private sealed record PayoutTarget(long UserId, PayoutMethod Method, decimal AmountUsd);
    }
}
