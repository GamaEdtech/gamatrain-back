namespace GamaEdtech.Data.Dto.Contribution
{
    using System;

    using GamaEdtech.Domain.Enumeration;

    public sealed class ContributionsDto
    {
        public long Id { get; set; }
        public string? Comment { get; set; }
        public CategoryType? CategoryType { get; set; }
        public Status Status { get; set; }
        public long? IdentifierId { get; set; }
        public string? CreationUser { get; set; }
        public DateTimeOffset CreationDate { get; set; }
        public string? Data { get; set; }
        public DateTimeOffset? LastModifyDate { get; set; }
    }
}
