namespace GamaEdtech.Presentation.Api.Areas.Admin.Controllers
{
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Common.DataAccess.Specification.Impl;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Data.Dto.ApplicationSettings;
    using GamaEdtech.Data.Dto.Transaction;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Domain.Specification;
    using GamaEdtech.Domain.Specification.Transaction;
    using GamaEdtech.Presentation.ViewModel.Transaction;

    using Microsoft.AspNetCore.Mvc;

    [Common.DataAnnotation.Area(nameof(Admin), "Admin")]
    [Route("api/v{version:apiVersion}/[area]/[controller]")]
    [ApiVersion("1.0")]
    [Permission(Roles = [nameof(Role.Admin)])]
    public class TransactionsController(Lazy<ILogger<TransactionsController>> logger, Lazy<ITransactionService> transactionService, Lazy<IApplicationSettingsService> applicationSettingsService
        , Lazy<IEmailService> emailService, Lazy<IIdentityService> identityService)
        : ApiControllerBase<TransactionsController>(logger)
    {
        [HttpGet, Produces<ApiResponse<ListDataSource<TransactionsListResponseViewModel>>>()]
        public async Task<IActionResult<ListDataSource<TransactionsListResponseViewModel>>> GetTransactions([NotNull, FromQuery] TransactionsListRequestViewModel request)
        {
            try
            {
                ISpecification<Transaction>? specification = null;

                if (request.IsDebit.HasValue)
                {
                    specification = new IsDebitEqualsSpecification(request.IsDebit.Value);
                }

                if (request.StartDate.HasValue || request.EndDate.HasValue)
                {
                    var spec = new CreationDateBetweenSpecification<Transaction>(request.StartDate, request.EndDate);
                    specification = specification is null ? spec : specification.And(spec);
                }

                if (request.UserId.HasValue)
                {
                    var spec = new UserIdEqualsSpecification<Transaction, long>(request.UserId.Value);
                    specification = specification is null ? spec : specification.And(spec);
                }

                if (request.IdentifierId.HasValue)
                {
                    var spec = new IdentifierIdEqualsSpecification<Transaction>(request.IdentifierId.Value);
                    specification = specification is null ? spec : specification.And(spec);
                }

                if (!string.IsNullOrEmpty(request.Name))
                {
                    var spec = new NameContainsSpecification(request.Name);
                    specification = specification is null ? spec : specification.And(spec);
                }

                if (!string.IsNullOrEmpty(request.Email))
                {
                    var spec = new EmailEqualsSpecification(request.Email);
                    specification = specification is null ? spec : specification.And(spec);
                }

                if (request.TransactionType is not null)
                {
                    var spec = new TransactionTypeEqualsSpecification(request.TransactionType);
                    specification = specification is null ? spec : specification.And(spec);
                }

                var result = await transactionService.Value.GetTransactionsAsync(new ListRequestDto<Transaction>
                {
                    PagingDto = request.PagingDto,
                    Specification = specification,
                });
                return Ok<ListDataSource<TransactionsListResponseViewModel>>(new()
                {
                    Errors = result.Errors,
                    Data = result.Data.List is null ? new() : new()
                    {
                        List = result.Data.List.Select(t => new TransactionsListResponseViewModel
                        {
                            Id = t.Id,
                            CreationDate = t.CreationDate,
                            CurrentBalance = t.CurrentBalance,
                            Description = t.Description,
                            IsDebit = t.IsDebit,
                            Points = t.Points,
                            UserId = t.UserId,
                            IdentifierId = t.IdentifierId,
                            TransactionType = t.TransactionType,
                        }),
                        TotalRecordsCount = result.Data.TotalRecordsCount,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ListDataSource<TransactionsListResponseViewModel>>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpPost, Produces<ApiResponse<CreateTransactionResponseViewModel>>()]
        public async Task<IActionResult<CreateTransactionResponseViewModel>> CreateTransaction([NotNull] CreateTransactionRequestViewModel request)
        {
            try
            {
                var dto = new CreateTransactionRequestDto()
                {
                    UserId = request.UserId.GetValueOrDefault(),
                    Points = request.Points.GetValueOrDefault(),
                    Description = $"{request.Description} - Admin CreateTransaction",
                    TransactionType = request.IsDebit.GetValueOrDefault() ? TransactionType.AdminDecreaseBalance : TransactionType.AdminIncreaseBalance,
                };
                var result = request.IsDebit.GetValueOrDefault()
                    ? await transactionService.Value.DecreaseBalanceAsync(dto)
                    : await transactionService.Value.IncreaseBalanceAsync(dto);
                if (result.OperationResult is not Constants.OperationResult.Succeeded)
                {
                    return Ok<CreateTransactionResponseViewModel>(new(result.Errors));
                }

                var balance = await transactionService.Value.GetCurrentBalanceAsync(new() { UserId = request.UserId.GetValueOrDefault() });

                var user = await identityService.Value.GetUserAsync(new IdEqualsSpecification<ApplicationUser, long>(request.UserId.GetValueOrDefault()));
                var template = (await applicationSettingsService.Value.GetSettingAsync<string?>(nameof(ApplicationSettingsDto.AdminTransactionCreationEmailTemplate))).Data;
                template = template?
                    .Replace("[RECEIVER_NAME]", $"{user.Data!.FirstName} {user.Data.LastName}", StringComparison.OrdinalIgnoreCase)
                    .Replace("[DESCRIPTION]", request.Description, StringComparison.OrdinalIgnoreCase)
                    .Replace("[POINTS]", request.Points.ToString(), StringComparison.OrdinalIgnoreCase)
                    .Replace("[CURRENT_BALANCE]", balance.Data.ToString(), StringComparison.OrdinalIgnoreCase);
                _ = await emailService.Value.SendEmailAsync(new()
                {
                    Subject = "Transaction Creation",
                    Body = template!,
                    EmailAddresses = [user.Data!.Email!],
                });

                return Ok<CreateTransactionResponseViewModel>(new(balance.Errors)
                {
                    Data = new() { Balance = balance.Data, },
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);
                return Ok<CreateTransactionResponseViewModel>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }
    }
}
