namespace GamaEdtech.Data.Dto.File
{
    /// <summary>A stored file: its name relative to its container (<c>2025/06/22/name.pdf</c>) and its size in bytes.</summary>
    public sealed class StoredFileDto
    {
        public required string Name { get; set; }

        public long Size { get; set; }
    }
}
