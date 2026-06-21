namespace GamaEdtech.Application.Service
{
    using System.Diagnostics.CodeAnalysis;
    using System.Threading.Tasks;

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
        , Lazy<IIdentityService> identityService, Lazy<IGenericFactory<ICurrencyConverterProvider, Currency>> currencyConverterFactory)
        : LocalizableServiceBase<PaymentService>(unitOfWorkProvider, httpContextAccessor, localizer, logger), IPaymentService
    {
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
                };
                repository.Add(payment);
                _ = await uow.SaveChangesAsync();

                var email = await identityService.Value.GetUsersEmailAsync(new IdEqualsSpecification<ApplicationUser, long>(requestDto.UserId));

                var result = await gatewayFactory.Value.GetProvider(requestDto.Gateway)!.CreateAsync(new()
                {
                    Amount = requestDto.Amount,
                    Currency = requestDto.Currency,
                    Description = requestDto.Description,
                    Title = requestDto.Title,
                    PaymentId = payment.Id,
                    Email = email.Data?[0],
                    CallbackUrl = $"{configuration.Value.GetValue<string>("PaymentGateway:CallbackBaseUrl")}/payments/{payment.Id}/verify",
                });
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
                .SetProperty(p => p.VerifyDate, DateTimeOffset.UtcNow));

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
    }
}
