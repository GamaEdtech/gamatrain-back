namespace GamaEdtech.Application.Interface
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.Tutorial;
    using GamaEdtech.Domain.Enumeration;

    /// <summary>
    /// The admin panel's tutorials, on this backend's copy: the list, one tutorial, adding, editing, its status, deleting and
    /// AI keywords. Every change is refused (<c>notNative</c>) while <c>Migration:WriteRoutes:Tutorials</c> is not
    /// <c>Native</c>: until then gama-api is where tutorials are edited, and the copy would overwrite a change made here.
    /// See docs/business/tutorials.md, "Admin".
    /// </summary>
    [Injectable]
    public interface ITutorialAdminService
    {
        Task<ResultData<TutorialAdminListDto>> GetListAsync([NotNull] TutorialAdminListRequestDto requestDto);

        Task<ResultData<TutorialAdminDto>> GetAsync(long id);

        /// <summary>Adds a tutorial owned by <paramref name="userId"/>; answers its id.</summary>
        Task<ResultData<long>> CreateAsync(long userId, [NotNull] TutorialEditDto requestDto);

        Task<ResultData<bool>> UpdateAsync(long id, [NotNull] TutorialEditDto requestDto);

        Task<ResultData<bool>> ChangeStatusAsync(long id, [NotNull] TutorialStatus status);

        /// <summary>Deletes a tutorial and its image rows (the image files stay in storage).</summary>
        Task<ResultData<bool>> DeleteAsync(long id);

        /// <summary>Asks the AI for the tutorial's keywords and learning clusters, and saves the keywords on it.</summary>
        Task<ResultData<TutorialKeywordsDto>> GenerateKeywordsAsync(long id);
    }
}
