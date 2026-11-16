namespace GamaEdtech.Application.Service
{
    using System.Diagnostics.CodeAnalysis;
    using System.Text.Json.Nodes;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Data.Dto.PastPaper;
    using GamaEdtech.Domain.Enumeration;
    using GamaEdtech.Infrastructure.Interface;

    public sealed class TeacherPastPaperService(Lazy<IMigrationRouter> migrationRouter) : ITeacherPastPaperService
    {
        public Task<ResultData<JsonObject>> GetPapersAsync([NotNull] TeacherPaperRequestDto requestDto) =>
            migrationRouter.Value.RunAsync(MigrationDomain.PastPapers, MigrationFeatures.Teacher, (IPastPaperSource source) => source.GetOwnPapersAsync(requestDto));

        public Task<ResultData<JsonObject>> GetPaperAsync([NotNull] TeacherPaperRequestDto requestDto) =>
            migrationRouter.Value.RunAsync(MigrationDomain.PastPapers, MigrationFeatures.Teacher, (IPastPaperSource source) => source.GetOwnPaperAsync(requestDto));
    }
}
