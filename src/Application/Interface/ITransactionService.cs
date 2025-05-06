namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.Transaction;
    using GamaEdtech.Domain.Entity;

    [Injectable]
    public interface ITransactionService
    {
        Task<ResultData<ListDataSource<TransactionDto>>> GetTransactionsAsync(ListRequestDto<Transaction>? requestDto = null);
        Task<ResultData<TransactionDto>> GetTransactionAsync([NotNull] ISpecification<Transaction> specification);
        Task<ResultData<long>> IncreaseBalanceAsync([NotNull] CreateTransactionRequestDto requestDto);
        Task<ResultData<long>> DecreaseBalanceAsync([NotNull] CreateTransactionRequestDto requestDto);
        Task<ResultData<int>> GetCurrentBalanceAsync([NotNull] GetCurrentBalanceRequestDto requestDto);
        Task<ResultData<IEnumerable<GetStatisticsResponseDto>>> GetStatisticsAsync([NotNull] GetStatisticsRequestDto requestDto);
    }
}
