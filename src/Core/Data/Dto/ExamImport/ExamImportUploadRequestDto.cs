namespace GamaEdtech.Data.Dto.ExamImport
{
    /// <summary>An image to upload to gama-api for a question.</summary>
    public sealed class ExamImportUploadRequestDto
    {
        /// <summary>The caller's gama-api token.</summary>
        public required string SecretKey { get; set; }

        /// <summary>Its name; gama-api checks the extension (jpg, jpeg, png).</summary>
        public required string FileName { get; set; }

        public required string ContentType { get; set; }

#pragma warning disable CA1819 // Properties should not return arrays
        public required byte[] Content { get; set; }
#pragma warning restore CA1819 // Properties should not return arrays
    }
}
