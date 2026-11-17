namespace GamaEdtech.Application.Interface
{
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Data.Dto.ExamCopy;

    /// <summary>
    /// Copies gama-api's exams (<c>azmoons</c>), questions (<c>azmoon_tests</c>), attempts (<c>azmoon_data</c>) and answer
    /// sheets (<c>azmoon_results</c>) into this backend, reading gama-api's database only, with gama-api's ids. Each run mirrors
    /// gama-api: new and changed rows are written (exams and questions by their <c>up_date</c>), rows gone from gama-api are
    /// removed. It runs every 15 minutes while exams are still written on gama-api, and refuses once
    /// <c>Migration:WriteRoutes:Exams</c> is <c>Native</c>. See docs/business/exams-migration.md.
    /// </summary>
    [Injectable]
    public interface IExamCopyService
    {
        Task<ResultData<ExamCopyResultDto>> CopyAsync();
    }
}
