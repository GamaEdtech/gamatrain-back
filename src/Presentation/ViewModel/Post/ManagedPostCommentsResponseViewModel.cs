namespace GamaEdtech.Presentation.ViewModel.Post
{
    using System;
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Domain.Enumeration;

    public sealed class ManagedPostCommentsResponseViewModel
    {
        public long Id { get; set; }

        public long PostId { get; set; }

        public string? PostTitle { get; set; }

        public string? Comment { get; set; }

        public string? CreationUser { get; set; }

        public DateTimeOffset CreationDate { get; set; }

        [JsonConverter(typeof(EnumerationConverter<Status, byte>))]
        public Status Status { get; set; }

        public string? RejectionComment { get; set; }
    }
}
