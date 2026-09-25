namespace GamaEdtech.Domain.Enumeration
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    public sealed class ExportFileType : Enumeration<ExportFileType, byte>
    {
        [Display]
        public static readonly ExportFileType Pdf = new(nameof(Pdf), 0, ".pdf");

        [Display]
        public static readonly ExportFileType Word = new(nameof(Word), 1, ".docx");

        [Display]
        public static readonly ExportFileType PowerPoint = new(nameof(PowerPoint), 2, ".pptx");

        /// <summary>A 496x792 WebP picture of the Pdf export's first page (2026-09-25).</summary>
        [Display]
        public static readonly ExportFileType Thumbnail = new(nameof(Thumbnail), 3, ".webp", "image/webp");

        public string Extension { get; }

        /// <summary>The download's Content-Type. The document formats keep the generic binary type they were
        /// always served with; the thumbnail is served as an image.</summary>
        public string ContentType { get; } = "application/octet-stream";

        public ExportFileType()
        {
        }

        public ExportFileType(string name, byte value, string extension, string contentType = "application/octet-stream")
            : base(name, value)
        {
            Extension = extension;
            ContentType = contentType;
        }
    }
}
