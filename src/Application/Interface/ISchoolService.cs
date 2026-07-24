namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Specification;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.Contribution;
    using GamaEdtech.Data.Dto.School;
    using GamaEdtech.Domain.Entity;

    using NetTopologySuite.Geometries;

    [Injectable]
    public interface ISchoolService
    {
        #region Schools

        Task<ResultData<ListDataSource<SchoolsDto>>> GetSchoolsAsync(ListRequestDto<School>? requestDto = null);
        Task<ResultData<ListDataSource<SchoolInfoDto>>> GetSchoolsListAsync(ListRequestDto<School>? requestDto = null, Point? point = null);
        Task<ResultData<SchoolDto>> GetSchoolAsync([NotNull] ISpecification<School> specification);
        Task<ResultData<IReadOnlyList<KeyValuePair<long, string?>>>> GetSchoolsNameAsync([NotNull] ISpecification<School> specification);
        Task<ResultData<long>> ManageSchoolAsync([NotNull] ManageSchoolRequestDto requestDto);
        Task<ResultData<bool>> RemoveSchoolAsync([NotNull] ISpecification<School> specification);
        Task<ResultData<bool>> ExistsSchoolAsync([NotNull] ISpecification<School> specification);
        Task IncreaseSchoolViewAsync(long id);

        #endregion

        #region Rate and Comments

        Task<ResultData<SchoolRateDto>> GetSchoolRateAsync([NotNull] ISpecification<SchoolComment> specification);
        Task<ResultData<ListDataSource<SchoolCommentDto>>> GetSchoolCommentsAsync(ListRequestDto<SchoolComment>? requestDto = null);
        Task<ResultData<bool>> LikeSchoolCommentAsync([NotNull] SchoolCommentReactionRequestDto requestDto);
        Task<ResultData<bool>> DislikeSchoolCommentAsync([NotNull] SchoolCommentReactionRequestDto requestDto);
        Task<ResultData<long>> CreateSchoolCommentContributionAsync([NotNull] ManageSchoolCommentContributionRequestDto requestDto);
        Task<ResultData<bool>> ConfirmSchoolCommentContributionAsync([NotNull] ConfirmSchoolCommentContributionRequestDto requestDto);
        Task<ResultData<bool>> CommentExistsAsync([NotNull] ISpecification<SchoolComment> specification);

        #endregion

        #region Images

        Task<ResultData<ListDataSource<SchoolImageDto>>> GetSchoolImagesAsync(ListRequestDto<SchoolImage>? requestDto = null);
        Task<ResultData<IEnumerable<SchoolImageInfoDto>>> GetSchoolImagesListAsync([NotNull] ISpecification<SchoolImage> specification);
        Task<ResultData<long>> CreateSchoolImageContributionAsync([NotNull] ManageSchoolImageContributionRequestDto requestDto);
        Task<ResultData<bool>> ConfirmSchoolImageContributionAsync([NotNull] ConfirmSchoolImageContributionRequestDto requestDto);
        Task<ResultData<bool>> RejectSchoolImageContributionAsync([NotNull] RejectContributionRequestDto requestDto);
        Task<ResultData<bool>> RemoveSchoolImageAsync([NotNull] ISpecification<SchoolImage> specification);
        Task<ResultData<bool>> ManageSchoolImageAsync([NotNull] ManageSchoolImageRequestDto requestDto);
        Task<ResultData<bool>> SetDefaultSchoolImageAsync([NotNull] SetDefaultSchoolImageRequestDto requestDto);
        Task<ResultData<long>> CreateRemoveSchoolImageContributionAsync([NotNull] CreateRemoveSchoolImageContributionRequestDto requestDto);
        Task<ResultData<bool>> ConfirmRemoveSchoolImageContributionAsync([NotNull] ConfirmRemoveSchoolImageContributionRequestDto requestDto);

        #endregion

        #region Contributions

        Task<ResultData<long>> ManageSchoolContributionAsync([NotNull] ManageSchoolContributionRequestDto requestDto);
        Task<ResultData<bool>> ConfirmSchoolContributionAsync([NotNull] ConfirmSchoolContributionRequestDto requestDto);
        Task<ResultData<bool>> RejectSchoolContributionAsync([NotNull] RejectContributionRequestDto requestDto);

        #endregion

        #region Issues

        Task<ResultData<long>> CreateSchoolIssuesContributionAsync([NotNull] CreateSchoolIssuesContributionRequestDto requestDto);
        Task<ResultData<bool>> ConfirmSchoolIssuesContributionAsync([NotNull] ConfirmSchoolIssuesContributionRequestDto requestDto);

        #endregion

        #region Job

        Task<ResultData<bool>> UpdateSchoolScoreAsync();
        Task<ResultData<bool>> UpdateSchoolCommentsRatingAsync();
        Task<ResultData<bool>> UpdateSchoolCommentReactionsAsync(long? schoolCommentId = null);
        Task<ResultData<bool>> RemoveOldRejectedSchoolImagesAsync();

        #endregion
    }
}
