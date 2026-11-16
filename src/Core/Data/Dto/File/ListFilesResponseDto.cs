namespace GamaEdtech.Data.Dto.File
{
    /// <summary>One page of stored files; <see cref="NextStartAfter"/> is null when there are no more.</summary>
    public sealed class ListFilesResponseDto
    {
        public IReadOnlyList<StoredFileDto> Files { get; set; } = [];

        public string? NextStartAfter { get; set; }
    }
}
