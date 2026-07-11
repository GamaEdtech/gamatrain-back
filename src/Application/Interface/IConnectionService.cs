namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.Connection;
    using GamaEdtech.Domain.Entity;

    [Injectable]
    public interface IConnectionService
    {
        Task<ResultData<ListDataSource<FollowDto>>> GetFollowersAsync(ListRequestDto<Connection>? requestDto = null);
        Task<ResultData<ListDataSource<FollowDto>>> GetFollowingsAsync(ListRequestDto<Connection>? requestDto = null);
        Task<ResultData<bool>> FollowAsync([NotNull] FollowRequestDto requestDto);
        Task<ResultData<bool>> UnFollowAsync([NotNull] UnFollowRequestDto requestDto);
        Task<ResultData<bool>> ToggleSubscriptionAsync([NotNull] ToggleSubscriptionRequestDto requestDto);
        Task<ResultData<ListDataSource<FollowRequestsResponseDto>>> GetFollowRequestsAsync([NotNull] FollowRequestsRequestDto requestDto);
        Task<ResultData<bool>> ConfirmFollowRequestAsync([NotNull] ConfirmFollowRequestRequestDto requestDto);
        Task<ResultData<bool>> RejectFollowRequestAsync([NotNull] RejectFollowRequestRequestDto requestDto);
        Task<ResultData<IEnumerable<ConnectionStatusDto>>> GetConnectionStatusAsync([NotNull] ConnectionStatusRequestDto requestDto);
    }
}

