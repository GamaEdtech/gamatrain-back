namespace GamaEdtech.Data.Dto.FaqManager
{
    using Microsoft.AspNetCore.Http;

    public record CreateForumDto(IEnumerable<string> ClassificationNodes,
    string SummaryOfQuestion, string Question, IEnumerable<IFormFile>? AttachFiles);
}
