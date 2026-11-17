namespace GamaEdtech.Application.Interface
{
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.Tutorial;

    /// <summary>
    /// Copies gama-api's tutorials (<c>dars</c>) and their images' rows (<c>tutorials_files</c>) into <c>Tutorials</c> and
    /// <c>TutorialImages</c>, reading gama-api's database only. Ids are kept; a run can be repeated and updates instead of
    /// duplicating. It refuses to run once <c>Migration:WriteRoutes:Tutorials</c> is <c>Native</c> (tutorials are then
    /// edited here). See docs/business/tutorials.md.
    /// </summary>
    [Injectable]
    public interface ITutorialImportService
    {
        Task<ResultData<TutorialImportResultDto>> ImportAsync();
    }
}
