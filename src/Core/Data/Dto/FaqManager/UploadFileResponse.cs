namespace GamaEdtech.Data.Dto.FaqManager
{
    using GamaEdtech.Domain.DataAccess.Responses.MediaResponses;

    public class UploadFileResult
    {
        public IEnumerable<UploadResponsePerProvider> UploadResponsePerProviders { get; set; } = [];

        public UploadResponsePerProvider? GetUploaderFileResultsAllUploaded() =>
            UploadResponsePerProviders.FirstOrDefault(c =>
            c.UploadFileResult.FileResults.All(a => a.FileUploadStatus));
    }

    public class UploadResponsePerProvider
    {
        public string UploaderName { get; init; }
        public UploadFileResponse UploadFileResult { get; init; }
    }
}
