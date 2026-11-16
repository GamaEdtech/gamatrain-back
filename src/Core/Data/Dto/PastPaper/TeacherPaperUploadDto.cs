namespace GamaEdtech.Data.Dto.PastPaper
{
    /// <summary>A file a teacher uploads for a past paper, before the paper is added or edited with it.</summary>
    public sealed class TeacherPaperUploadDto
    {
        /// <summary>The caller (this backend's user id).</summary>
        public long UserId { get; set; }

        /// <summary>The caller's token, forwarded to gama-api while the route is <c>Legacy</c>.</summary>
        public required string Token { get; set; }

        public required string FileName { get; set; }

        public required string ContentType { get; set; }

#pragma warning disable CA1819 // The uploaded bytes, as the file provider takes them.
        public required byte[] Content { get; set; }
#pragma warning restore CA1819

        /// <summary>Which of the paper's files it is (<c>pdf</c> for the question PDF), when the form says so: the question PDF
        /// also gets its thumbnail.</summary>
        public string? Kind { get; set; }
    }
}
