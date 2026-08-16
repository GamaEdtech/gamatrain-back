namespace GamaEdtech.Application.Service
{
    using System.Diagnostics.CodeAnalysis;
    using System.Threading.Tasks;

    using EntityFramework.Exceptions.Common;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Core.Extensions.Linq;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Common.DataAccess.Specification.Impl;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Common.Service;
    using GamaEdtech.Common.Service.Factory;
    using GamaEdtech.Data.Dto.Payment;
    using GamaEdtech.Data.Dto.Provider.PaymentGateway;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.Interface;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging;

    using static GamaEdtech.Common.Core.Constants;

    public class PaymentService(Lazy<IUnitOfWorkProvider> unitOfWorkProvider, Lazy<IHttpContextAccessor> httpContextAccessor, Lazy<IStringLocalizer<PaymentService>> localizer
        , Lazy<ILogger<PaymentService>> logger, Lazy<IGenericFactory<IPaymentGatewayProvider, PaymentGateway>> gatewayFactory, Lazy<IConfiguration> configuration, Lazy<ITransactionService> transactionService
        , Lazy<IIdentityService> identityService, Lazy<IGenericFactory<ICurrencyConverterProvider, Currency>> currencyConverterFactory, Lazy<ISubscriptionQuotaService> subscriptionQuotaService
        , Lazy<IGenericFactory<IRecurringPaymentGatewayProvider, PaymentGateway>> recurringGatewayFactory)
        : LocalizableServiceBase<PaymentService>(unitOfWorkProvider, httpContextAccessor, localizer, logger), IPaymentService
    {
        /// <summary>
        /// Locks the base-currency (USD) reporting amount at verify time. Stablecoins pegged 1:1 to
        /// USD convert directly; other currencies (SOL, GET) have no reliable peg yet and are left
        /// null until a real FX source is introduced.
        /// </summary>
        private static (decimal? BaseCurrencyAmount, decimal? ExchangeRate) ResolveBaseCurrency(Currency currency, decimal amount) =>
            currency == Currency.USD || currency == Currency.USDC || currency == Currency.USDT
                ? (amount, 1m)
                : (null, null);

        public async Task<ResultData<ListDataSource<PaymentDto>>> GetPaymentsAsync(ListRequestDto<Payment>? requestDto = null)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var result = await uow.GetRepository<Payment>().GetManyQueryable(requestDto?.Specification).FilterListAsync(requestDto?.PagingDto);
                var users = await result.List.Select(t => new PaymentDto
                {
                    Id = t.Id,
                    CreationDate = t.CreationDate,
                    VerifyDate = t.VerifyDate,
                    UserId = t.UserId,
                    FirstName = t.User!.FirstName,
                    LastName = t.User.LastName,
                    City = t.User.City != null ? t.User.City.Title : null,
                    State = t.User.City != null && t.User.City.Parent != null ? t.User.City.Parent.Title : null,
                    Country = t.User.City != null && t.User.City.Parent != null && t.User.City.Parent.Parent != null ? t.User.City.Parent.Parent.Title : null,
                    Currency = t.Currency,
                    Amount = t.Amount,
                    Status = t.Status,
                    SourceWallet = t.SourceWallet,
                    Comment = t.Comment,
                    TransactionId = t.TransactionId,
                    Gateway = t.Gateway,
                }).ToListAsync();
                return new(OperationResult.Succeeded) { Data = new() { List = users, TotalRecordsCount = result.TotalRecordsCount } };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<CreatePaymentResponseDto>> CreatePaymentAsync([NotNull] CreatePaymentRequestDto requestDto)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var repository = uow.GetRepository<Payment, long>();

                var payment = new Payment
                {
                    Amount = requestDto.Amount,
                    Currency = requestDto.Currency,
                    UserId = requestDto.UserId,
                    CreationDate = DateTimeOffset.UtcNow,
                    Status = PaymentStatus.Pending,
                    Gateway = requestDto.Gateway,
                    UserSubscriptionId = requestDto.UserSubscriptionId,
                };
                repository.Add(payment);
                _ = await uow.SaveChangesAsync();

                var email = await identityService.Value.GetUsersEmailAsync(new IdEqualsSpecification<ApplicationUser, long>(requestDto.UserId));
                var callbackUrl = $"{configuration.Value.GetValue<string>("PaymentGateway:CallbackBaseUrl")}/payments/{payment.Id}/verify";

                // Only a gateway that both supports recurring billing (Stripe today, GamaTrain never will) and
                // was asked to resolve a specific plan price takes the recurring-checkout path - a plain points
                // top-up (no SubscriptionPlanPriceId) always takes the one-time path below, on any gateway.
                var recurringProvider = requestDto.SubscriptionPlanPriceId.HasValue ? recurringGatewayFactory.Value.GetProvider(requestDto.Gateway) : null;

                ResultData<CreateResponseDto> result;
                if (recurringProvider is not null)
                {
                    if (requestDto.UserSubscriptionId is null)
                    {
                        // Defensive: SubscriptionPlanPriceId is only ever set alongside UserSubscriptionId (both
                        // come from PurchaseSubscriptionAsync together) - guard rather than crash on a caller bug.
                        return new(OperationResult.NotValid) { Errors = [new() { Message = Localizer.Value["InvalidRequest"], }] };
                    }

                    var mapping = await uow.GetRepository<SubscriptionPlanGatewayMapping>()
                        .GetManyQueryable(t => t.SubscriptionPlanPriceId == requestDto.SubscriptionPlanPriceId && t.Gateway == requestDto.Gateway)
                        .Select(t => new { t.ExternalPlanId })
                        .FirstOrDefaultAsync();

                    result = mapping?.ExternalPlanId is null
                        ? new(OperationResult.NotValid) { Errors = [new() { Message = Localizer.Value["RecurringGatewayMappingMissing"], }] }
                        : await recurringProvider.CreateSubscriptionCheckoutAsync(new()
                        {
                            PaymentId = payment.Id,
                            UserSubscriptionId = requestDto.UserSubscriptionId.Value,
                            ExternalPriceId = mapping.ExternalPlanId,
                            Email = email.Data?[0],
                            CallbackUrl = callbackUrl,
                        });
                }
                else
                {
                    result = await gatewayFactory.Value.GetProvider(requestDto.Gateway)!.CreateAsync(new()
                    {
                        Amount = requestDto.Amount,
                        Currency = requestDto.Currency,
                        Description = requestDto.Description,
                        Title = requestDto.Title,
                        PaymentId = payment.Id,
                        Email = email.Data?[0],
                        CallbackUrl = callbackUrl,
                    });
                }

                if (result.OperationResult is OperationResult.Succeeded)
                {
                    payment.TransactionId = result.Data?.TransactionId;
                    _ = await uow.SaveChangesAsync();

                    return new(OperationResult.Succeeded)
                    {
                        Data = new()
                        {
                            PaymentId = payment.Id,
                            Url = result.Data?.Url,
                        }
                    };
                }

                var message = result.Errors?.FirstOrDefault().Message;
                _ = await repository.GetManyQueryable(t => t.Id == payment.Id).ExecuteUpdateAsync(t => t
                    .SetProperty(p => p.Status, PaymentStatus.Failed)
                    .SetProperty(p => p.Comment, message)
                    .SetProperty(p => p.VerifyDate, DateTimeOffset.UtcNow));
                return new(OperationResult.Failed) { Errors = result.Errors };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message, }] };
            }
        }

        public async Task<ResultData<bool>> VerifyPaymentAsync([NotNull] VerifyPaymentRequestDto requestDto)
        {
            var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
            var repository = uow.GetRepository<Payment, long>();

            var payment = await repository.GetManyQueryable(t => t.Id == requestDto.Id).Select(t => new
            {
                t.Status,
                t.Currency,
                t.Amount,
                t.Gateway,
                t.UserId,
                t.UserSubscriptionId,
            }).FirstOrDefaultAsync();
            if (payment is null)
            {
                return new(OperationResult.NotFound)
                {
                    Errors = [new() { Message = Localizer.Value["PaymentNotFound"] },],
                };
            }

            if (payment.Status != PaymentStatus.Pending)
            {
                return new(OperationResult.NotFound)
                {
                    Errors = [new() { Message = Localizer.Value["InvalidPaymentStatus"] },],
                };
            }

            var result = await gatewayFactory.Value.GetProvider(payment.Gateway)!.VerifyAsync(new()
            {
                PaymentId = requestDto.Id,
                Amount = payment.Amount,
                Currency = payment.Currency,
                TransactionId = requestDto.TransactionId,
            });
            if (result.OperationResult is not OperationResult.Succeeded)
            {
                var comment = result.Errors?.FirstOrDefault().Message;
                _ = await repository.GetManyQueryable(t => t.Id == requestDto.Id).ExecuteUpdateAsync(t => t
                        .SetProperty(p => p.Status, PaymentStatus.Failed)
                        .SetProperty(p => p.TransactionId, requestDto.TransactionId)
                        .SetProperty(p => p.Comment, comment)
                        .SetProperty(p => p.VerifyDate, DateTimeOffset.UtcNow));

                return new(result.OperationResult) { Errors = result.Errors, };
            }

            var (baseCurrencyAmount, exchangeRate) = ResolveBaseCurrency(payment.Currency, payment.Amount);

            if (payment.UserSubscriptionId.HasValue)
            {
                using var subscriptionTrn = uow.CreateTransactionScope();

                // Guarded on Pending: a concurrent/duplicate verify call can affect at most one of these.
                var paymentUpdated = await repository.GetManyQueryable(t => t.Id == requestDto.Id && t.Status == PaymentStatus.Pending).ExecuteUpdateAsync(t => t
                    .SetProperty(p => p.Status, PaymentStatus.Paid)
                    .SetProperty(p => p.SourceWallet, result.Data!.SourceWallet)
                    .SetProperty(p => p.TransactionId, requestDto.TransactionId)
                    .SetProperty(p => p.VerifyDate, DateTimeOffset.UtcNow)
                    .SetProperty(p => p.BaseCurrencyAmount, baseCurrencyAmount)
                    .SetProperty(p => p.ExchangeRate, exchangeRate));
                if (paymentUpdated == 0)
                {
                    return new(OperationResult.NotFound) { Errors = [new() { Message = Localizer.Value["InvalidPaymentStatus"] },] };
                }

                var activation = await subscriptionQuotaService.Value.ActivateSubscriptionAsync(new()
                {
                    UserSubscriptionId = payment.UserSubscriptionId.Value,
                    ExternalSubscriptionId = result.Data?.ExternalSubscriptionId,
                });
                if (activation.OperationResult is not OperationResult.Succeeded)
                {
                    return new(activation.OperationResult) { Errors = activation.Errors };
                }

                subscriptionTrn.Complete();

                return new(OperationResult.Succeeded) { Data = true };
            }

            var points = await currencyConverterFactory.Value.GetProvider(payment.Currency)!.GetPointsAsync(new()
            {
                Amount = payment.Amount,
                Mint = result.Data!.Mint,
            });
            if (points.OperationResult is not OperationResult.Succeeded)
            {
                return new(points.OperationResult) { Errors = points.Errors, };
            }

            using var trn = uow.CreateTransactionScope();

            _ = await repository.GetManyQueryable(t => t.Id == requestDto.Id).ExecuteUpdateAsync(t => t
                .SetProperty(p => p.Status, PaymentStatus.Paid)
                .SetProperty(p => p.SourceWallet, result.Data!.SourceWallet)
                .SetProperty(p => p.TransactionId, requestDto.TransactionId)
                .SetProperty(p => p.VerifyDate, DateTimeOffset.UtcNow)
                .SetProperty(p => p.BaseCurrencyAmount, baseCurrencyAmount)
                .SetProperty(p => p.ExchangeRate, exchangeRate));

            _ = await transactionService.Value.IncreaseBalanceAsync(new()
            {
                UserId = payment.UserId,
                Description = "Payment",
                IdentifierId = requestDto.Id,
                Points = points.Data!.Point,
                TransactionType = TransactionType.Payment,
            });

            trn.Complete();

            return new(OperationResult.Succeeded)
            {
                Data = true,
            };
        }

        public async Task<ResultData<List<PaymentsSummaryDto>>> GetPaymentsSummaryAsync(ISpecification<Payment>? specification)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var result = await uow.GetRepository<Payment>().GetManyQueryable(specification).GroupBy(t => new { t.CreationDate.Date, t.Status })
                    .Select(t => new PaymentsSummaryDto
                    {
                        Date = t.Key.Date,
                        Status = t.Key.Status,
                        Amount = t.Sum(p => p.Amount),
                        Count = t.Count(),
                    }).OrderBy(t => t.Date).ToListAsync();

                return new(OperationResult.Succeeded) { Data = result };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Errors = [new() { Message = exc.Message },] };
            }
        }

        public async Task<ResultData<bool>> HandleRecurringWebhookAsync(PaymentGateway gateway, [NotNull] HttpRequest request)
        {
            try
            {
                var provider = recurringGatewayFactory.Value.GetProvider(gateway);
                if (provider is null)
                {
                    // Not a gateway that supports recurring billing at all - nothing to do, not an error.
                    return new(OperationResult.Succeeded) { Data = true };
                }

                var parsed = await provider.ParseWebhookEventAsync(request);
                if (parsed.OperationResult is not OperationResult.Succeeded || parsed.Data is null)
                {
                    return new(parsed.OperationResult) { Data = false, Errors = parsed.Errors };
                }

                switch (parsed.Data.EventType)
                {
                    case RecurringWebhookEventType.InvoicePaid when parsed.Data.UserSubscriptionId is long userSubscriptionId:
                        return await HandleInvoicePaidAsync(gateway, userSubscriptionId, parsed.Data.ExternalTransactionId);

                    case RecurringWebhookEventType.PlanChangeInvoicePaid when parsed.Data.UserSubscriptionId is long switchUserSubscriptionId:
                        return await HandlePlanChangeInvoicePaidAsync(gateway, switchUserSubscriptionId, parsed.Data.ExternalTransactionId, parsed.Data.Amount);

                    case RecurringWebhookEventType.SubscriptionEnded when parsed.Data.UserSubscriptionId is long userSubscriptionId:
                        var cancellation = await subscriptionQuotaService.Value.CancelSubscriptionAsync(userSubscriptionId);
                        return new(cancellation.OperationResult) { Data = cancellation.Data, Errors = cancellation.Errors };

                    case RecurringWebhookEventType.PaymentFailed when parsed.Data.UserSubscriptionId is long failedUserSubscriptionId:
                        return await HandlePaymentFailedAsync(failedUserSubscriptionId);

                    default:
                        // Ignored, or a recognized event with no resolvable UserSubscriptionId (metadata
                        // missing/corrupt) - not something a client is waiting on, so no-op rather than making
                        // the gateway retry a webhook that will never succeed differently.
                        return new(OperationResult.Succeeded) { Data = true };
                }
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Data = false, Errors = [new() { Message = exc.Message, }] };
            }
        }

        /// <summary>
        /// Records the period's Payment row and renews the subscription - only for the *first* delivery of a
        /// given invoice. <c>Payment.TransactionId</c>+<c>Gateway</c>'s existing unique index is the idempotency
        /// guard against webhook redelivery: a duplicate insert throws <see cref="UniqueConstraintException"/>,
        /// caught below - critically, the renewal call is skipped in that case too, not just the insert, or a
        /// redelivered event would extend ExpirationDate a second time for the same period.
        /// </summary>
        private async Task<ResultData<bool>> HandleInvoicePaidAsync(PaymentGateway gateway, long userSubscriptionId, string? externalTransactionId)
        {
            var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
            var subscriptionInfo = await uow.GetRepository<UserSubscription>()
                .GetManyQueryable(t => t.Id == userSubscriptionId)
                .Select(t => new { t.UserId, t.PricePaid, t.Currency })
                .FirstOrDefaultAsync();
            if (subscriptionInfo is null)
            {
                // The webhook's own metadata pointed at a UserSubscriptionId that doesn't exist locally -
                // nothing to record a payment against. Shouldn't happen (we set that metadata ourselves at
                // checkout), logged for visibility, treated as a no-op rather than erroring the webhook.
                Logger.Value.LogWarning("Recurring webhook InvoicePaid for unknown UserSubscriptionId {UserSubscriptionId}", userSubscriptionId);
                return new(OperationResult.Succeeded) { Data = true };
            }

            var (baseCurrencyAmount, exchangeRate) = ResolveBaseCurrency(subscriptionInfo.Currency, subscriptionInfo.PricePaid);

            bool isFirstDeliveryOfThisInvoice;
            try
            {
                uow.GetRepository<Payment>().Add(new()
                {
                    UserId = subscriptionInfo.UserId,
                    Amount = subscriptionInfo.PricePaid,
                    Currency = subscriptionInfo.Currency,
                    Status = PaymentStatus.Paid,
                    Gateway = gateway,
                    CreationDate = DateTimeOffset.UtcNow,
                    VerifyDate = DateTimeOffset.UtcNow,
                    TransactionId = externalTransactionId,
                    UserSubscriptionId = userSubscriptionId,
                    BaseCurrencyAmount = baseCurrencyAmount,
                    ExchangeRate = exchangeRate,
                });
                _ = await uow.SaveChangesAsync();
                isFirstDeliveryOfThisInvoice = true;
            }
            catch (UniqueConstraintException)
            {
                isFirstDeliveryOfThisInvoice = false;
            }

            if (!isFirstDeliveryOfThisInvoice)
            {
                // Already recorded (and already renewed) by an earlier delivery of this same invoice event.
                return new(OperationResult.Succeeded) { Data = true };
            }

            var renewal = await subscriptionQuotaService.Value.RenewSubscriptionAsync(userSubscriptionId);
            return new(renewal.OperationResult) { Data = renewal.Data, Errors = renewal.Errors };
        }

        /// <summary>
        /// Records the Payment row for an immediate plan/interval switch's prorated invoice - only for the
        /// *first* delivery (same <c>Payment.TransactionId</c>+<c>Gateway</c> unique-index idempotency guard as
        /// <see cref="HandleInvoicePaidAsync"/>). Deliberately never calls <see cref="ISubscriptionQuotaService.
        /// RenewSubscriptionAsync"/>: unlike an ordinary renewal, this invoice doesn't represent a new billing
        /// period - <c>SubscriptionQuotaService.ApplyPlanSwitchAsync</c> already applied the plan/price/quota
        /// change synchronously when the switch itself was requested, well before this webhook arrives; calling
        /// Renew here would incorrectly push <c>ExpirationDate</c> forward by a full extra period and reset
        /// quota <c>Used</c> to 0 as a side effect of a mid-cycle upgrade. <paramref name="amount"/> is the
        /// invoice's own actually-charged amount (see <see cref="RecurringWebhookEventDto.Amount"/>'s doc
        /// comment for why this can't reuse <c>UserSubscription.PricePaid</c> the way
        /// <see cref="HandleInvoicePaidAsync"/> does).
        /// </summary>
        private async Task<ResultData<bool>> HandlePlanChangeInvoicePaidAsync(PaymentGateway gateway, long userSubscriptionId, string? externalTransactionId, decimal? amount)
        {
            var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
            var subscriptionInfo = await uow.GetRepository<UserSubscription>()
                .GetManyQueryable(t => t.Id == userSubscriptionId)
                .Select(t => new { t.UserId, t.Currency })
                .FirstOrDefaultAsync();
            if (subscriptionInfo is null)
            {
                // The webhook's own metadata pointed at a UserSubscriptionId that doesn't exist locally -
                // nothing to record a payment against. Shouldn't happen (we set that metadata ourselves at
                // checkout), logged for visibility, treated as a no-op rather than erroring the webhook.
                Logger.Value.LogWarning("Recurring webhook PlanChangeInvoicePaid for unknown UserSubscriptionId {UserSubscriptionId}", userSubscriptionId);
                return new(OperationResult.Succeeded) { Data = true };
            }

            var resolvedAmount = amount ?? 0;
            var (baseCurrencyAmount, exchangeRate) = ResolveBaseCurrency(subscriptionInfo.Currency, resolvedAmount);

            try
            {
                uow.GetRepository<Payment>().Add(new()
                {
                    UserId = subscriptionInfo.UserId,
                    Amount = resolvedAmount,
                    Currency = subscriptionInfo.Currency,
                    Status = PaymentStatus.Paid,
                    Gateway = gateway,
                    CreationDate = DateTimeOffset.UtcNow,
                    VerifyDate = DateTimeOffset.UtcNow,
                    TransactionId = externalTransactionId,
                    UserSubscriptionId = userSubscriptionId,
                    BaseCurrencyAmount = baseCurrencyAmount,
                    ExchangeRate = exchangeRate,
                });
                _ = await uow.SaveChangesAsync();
            }
            catch (UniqueConstraintException)
            {
                // Already recorded by an earlier delivery of this same invoice event - benign redelivery,
                // nothing else to do (no renewal/quota step to skip a second time, unlike HandleInvoicePaidAsync).
            }

            return new(OperationResult.Succeeded) { Data = true };
        }

        /// <summary>
        /// Visibility only - stamps <see cref="UserSubscription.LastPaymentFailedDate"/> so a client can prompt
        /// the user to update their card while Stripe's own Smart Retries are still ongoing. Deliberately never
        /// touches <see cref="UserSubscription.Status"/>/<see cref="UserSubscription.ExpirationDate"/> or quota -
        /// access is unaffected until either ExpirationDate naturally passes or Stripe's retries exhaust and
        /// fire customer.subscription.deleted (unchanged, see HandleRecurringWebhookAsync's SubscriptionEnded
        /// case). Guarded on Active, same idempotency style as CancelSubscriptionAsync - a stray delivery for a
        /// subscription that's already Expired/Cancelled/Pending is a safe no-op, not an error.
        /// </summary>
        private async Task<ResultData<bool>> HandlePaymentFailedAsync(long userSubscriptionId)
        {
            try
            {
                var uow = UnitOfWorkProvider.Value.CreateUnitOfWork();
                var affected = await uow.GetRepository<UserSubscription>()
                    .GetManyQueryable(t => t.Id == userSubscriptionId && t.Status == UserSubscriptionStatus.Active)
                    .ExecuteUpdateAsync(t => t.SetProperty(p => p.LastPaymentFailedDate, DateTimeOffset.UtcNow));

                return new(OperationResult.Succeeded) { Data = affected > 0 };
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return new(OperationResult.Failed) { Data = false, Errors = [new() { Message = exc.Message, }] };
            }
        }
    }
}
