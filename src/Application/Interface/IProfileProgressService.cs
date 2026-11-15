namespace GamaEdtech.Application.Interface
{
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.ProfileProgress;

    [Injectable]
    public interface IProfileProgressService
    {
        /// <summary>The user's own progress (level, quests, points and rank, practice streak, school goal).</summary>
        Task<ResultData<ProfileProgressDto>> GetProgressAsync(long userId);
    }
}
