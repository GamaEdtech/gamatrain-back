namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.School;
    using GamaEdtech.Domain.Enumeration;

    /// <summary>Users' connections to schools. See docs/business/schools-directory.md, "School members".</summary>
    [Injectable]
    public interface ISchoolMemberService
    {
        Task<ResultData<SchoolMembersDto>> GetSchoolMembersAsync(long schoolId);
        Task<ResultData<IEnumerable<UserSchoolDto>>> GetUserSchoolsAsync(long userId);
        Task<ResultData<bool>> JoinSchoolAsync([NotNull] JoinSchoolRequestDto requestDto);
        Task<ResultData<bool>> LeaveSchoolAsync(long userId, long schoolId, [NotNull] SchoolMemberRole role);

        /// <summary>The admin report: every membership, newest first, with the member and school, filtered, and the count per
        /// role of all matching ones. Admins only (names and emails).</summary>
        Task<ResultData<SchoolMemberAdminListDto>> GetMembershipsAsync([NotNull] SchoolMemberAdminListRequestDto requestDto);
    }
}
