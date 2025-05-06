namespace GamaEdtech.Presentation.Api.Controllers
{
    using System.Diagnostics.CodeAnalysis;

    using Asp.Versioning;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Core;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Common.Identity;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Domain.Specification;
    using GamaEdtech.Domain.Specification.Transaction;
    using GamaEdtech.Presentation.ViewModel.Transaction;

    using Microsoft.AspNetCore.Mvc;

    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("1.0")]
    [Permission(policy: null)]
    public class TransactionsController(Lazy<ILogger<TransactionsController>> logger, Lazy<ITransactionService> transactionService)
        : ApiControllerBase<TransactionsController>(logger)
    {
        [HttpGet, Produces<ApiResponse<ListDataSource<TransactionsResponseViewModel>>>()]
        public async Task<IActionResult<ListDataSource<TransactionsResponseViewModel>>> GetTransactions([NotNull, FromQuery] TransactionsRequestViewModel request)
        {
            try
            {
                ISpecification<Transaction> specification = new UserIdEqualsSpecification<Transaction, int>(User.UserId());

                if (request.IsDebit.HasValue)
                {
                    specification = specification.And(new IsDebitEqualsSpecification(request.IsDebit.Value));
                }

                var result = await transactionService.Value.GetTransactionsAsync(new ListRequestDto<Transaction>
                {
                    PagingDto = request.PagingDto,
                    Specification = specification,
                });
                return Ok<ListDataSource<TransactionsResponseViewModel>>(new()
                {
                    Errors = result.Errors,
                    Data = result.Data.List is null ? new() : new()
                    {
                        List = result.Data.List.Select(t => new TransactionsResponseViewModel
                        {
                            Id = t.Id,
                            CreationDate = t.CreationDate,
                            CurrentBalance = t.CurrentBalance,
                            Description = t.Description,
                            IsDebit = t.IsDebit,
                            Points = t.Points,
                        }),
                        TotalRecordsCount = result.Data.TotalRecordsCount,
                    }
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<ListDataSource<TransactionsResponseViewModel>>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpGet("balance"), Produces<ApiResponse<int>>()]
        public async Task<IActionResult<int>> GetCurrentBalance()
        {
            try
            {
                var result = await transactionService.Value.GetCurrentBalanceAsync(new() { UserId = User.UserId() });
                return Ok<int>(new()
                {
                    Errors = result.Errors,
                    Data = result.Data
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<int>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }

        [HttpGet("statistics"), Produces<ApiResponse<IEnumerable<TransactionStatisticsResponseViewModel>>>()]
        public async Task<IActionResult<IEnumerable<TransactionStatisticsResponseViewModel>>> GetStatistics([NotNull, FromQuery] TransactionStatisticsRequestViewModel request)
        {
            try
            {
                var now = DateTime.Now;
                if (!request.EndDate.HasValue)
                {
                    request.EndDate = DateOnly.FromDateTime(now);
                }

                if (!request.StartDate.HasValue)
                {
                    if (request.Period == Period.DayOfWeek)
                    {
                        request.StartDate = request.EndDate.Value.AddDays(-6);
                    }
                    else if (request.Period == Period.MonthOfYear)
                    {
                        request.StartDate = request.EndDate.Value.AddMonths(-11);
                    }
                }

                var result = await transactionService.Value.GetStatisticsAsync(new()
                {
                    UserId = User.UserId(),
                    Period = request.Period,
                    StartDate = request.StartDate.GetValueOrDefault(),
                    EndDate = request.EndDate.GetValueOrDefault(),
                });

                return Ok<IEnumerable<TransactionStatisticsResponseViewModel>>(new(result.Errors)
                {
                    Data = result.Data?.Select(t => new TransactionStatisticsResponseViewModel
                    {
                        Name = t.Name,
                        DebitValue = t.DebitValue,
                        CreditValue = t.CreditValue,
                    }),
                });
            }
            catch (Exception exc)
            {
                Logger.Value.LogException(exc);

                return Ok<IEnumerable<TransactionStatisticsResponseViewModel>>(new() { Errors = [new() { Message = exc.Message }] });
            }
        }
    }
}
