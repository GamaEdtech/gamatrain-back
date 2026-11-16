namespace GamaEdtech.Data.Dto.File
{
    using GamaEdtech.Domain.Enumeration;

    /// <summary>One page of the files stored in a container, in name order (ordinal).</summary>
    public sealed class ListFilesRequestDto
    {
        public required ContainerType ContainerType { get; set; }

        /// <summary>Names after this one (relative to the container); null for the first page.</summary>
        public string? StartAfter { get; set; }

        /// <summary>At most this many names (1-1000).</summary>
        public int MaxCount { get; set; } = 1000;
    }
}
