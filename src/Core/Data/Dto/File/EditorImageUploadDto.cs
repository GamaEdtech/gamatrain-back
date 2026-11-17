namespace GamaEdtech.Data.Dto.File
{
    /// <summary>An image a signed-in user inserts in a rich editor.</summary>
    public sealed class EditorImageUploadDto
    {
        public long UserId { get; set; }

        public required string FileName { get; set; }

#pragma warning disable CA1819 // Properties should not return arrays
        public required byte[] Content { get; set; }
#pragma warning restore CA1819 // Properties should not return arrays
    }
}
