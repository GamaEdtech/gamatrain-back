namespace GamaEdtech.Domain.Enumeration
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>
    /// Which of gama-api's three download-URL endpoints POST downloads/{id}/type/... resolves
    /// against. Deliberately separate from the broader ContentType (used by the unrelated
    /// games/spends endpoint, which also has a Test member) so this feature's API surface -
    /// including its Swagger schema - only ever exposes the 3 values it actually supports.
    /// </summary>
    public sealed class DownloadContentType : Enumeration<DownloadContentType, byte>
    {
        [Display]
        public static readonly DownloadContentType PastPaper = new(nameof(PastPaper), 1);

        [Display]
        public static readonly DownloadContentType Multimedia = new(nameof(Multimedia), 2);

        [Display]
        public static readonly DownloadContentType Exam = new(nameof(Exam), 3);

        public DownloadContentType()
        {
        }

        public DownloadContentType(string name, byte value) : base(name, value)
        {
        }
    }
}
