namespace GamaEdtech.Application.Interface
{
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.School;

    /// <summary>
    /// AI help with school comments, through <see cref="IAiService"/>. The prompts are built here from the school's own
    /// data or the admin's text, so callers can't use these to run any other prompt.
    /// </summary>
    [Injectable]
    public interface ISchoolAiService
    {
        /// <summary>A draft comment and ratings about a school, for a signed-in user to edit before posting.</summary>
        Task<ResultData<SchoolAiCommentDto>> DraftCommentAsync(long schoolId);

        /// <summary>An admin's comment for rejecting a school contribution, polished: same meaning, clearer and politer.</summary>
        Task<ResultData<string>> PolishRejectCommentAsync(string? comment);
    }
}
