namespace GamaEdtech.Data.Dto.School
{
    using GamaEdtech.Domain.Enumeration;

    public sealed class SchoolImageDto
    {
        public long Id { get; set; }
        public string? CreationUser { get; set; }
        public string? CreationUserAvatarUri { get; set; }
        public DateTimeOffset CreationDate { get; set; }
        public string? FileUri { get; set; }
        public ImageFileType? FileType { get; set; }
        public long SchoolId { get; set; }
        public string? SchoolName { get; set; }
        public long? TagId { get; set; }
        public bool IsDefault { get; set; }
    }
}
