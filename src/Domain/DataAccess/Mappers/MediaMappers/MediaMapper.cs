namespace GamaEdtech.Domain.DataAccess.Mappers.MediaMappers
{
    using GamaEdtech.Domain.DataAccess.Responses.MediaResponses;
    using GamaEdtech.Domain.Entity;

    using System.Diagnostics.CodeAnalysis;

    public static class MediaMapper
    {
        public static IEnumerable<FileResponse> MapToResult([NotNull] this IReadOnlyCollection<Media> media) => media.Select(s => new FileResponse
        {
            ContentType = s.ContentType,
            FileAddress = s.FileAddress,
            FileName = s.FileName,
            Url = s.FileAddress,
            CreateDate = s.CreateDate,
            LastUpdatedDate = s.LastUpdatedDate
        });
    }
}
