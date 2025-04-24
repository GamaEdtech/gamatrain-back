namespace GamaEdtech.Data.Dto.FaqManager
{
    using Microsoft.AspNetCore.Http;

    public record CreateForumDto(IEnumerable<string> FaqCategoryTitles,
    string SummaryOfQuestion, string Question, IEnumerable<IFormFile> AttachFiles);
}
