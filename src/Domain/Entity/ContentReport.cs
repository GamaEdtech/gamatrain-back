namespace GamaEdtech.Domain.Entity
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAccess.Entities;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.DataAnnotation.Schema;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    /// <summary>
    /// A "report a problem" a signed-in user sends about a paper, exam, exam question, tutorial, multimedia file or Q&amp;A
    /// (2026-11-23; gama-api's <c>reports</c> before). Staff resolve or dismiss it. See docs/business/support-and-social.md,
    /// "Content reports".
    /// </summary>
    [Table(nameof(ContentReport))]
    public class ContentReport : IEntity<ContentReport, long>, IUserId<long>
    {
        public const int MinMessageLength = 25;
        public const int MaxMessageLength = 500;

        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Long)]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Required]
        public long Id { get; set; }

        [Column(nameof(ContentType), DataType.Byte)]
        [Required]
        public ReportedContentType ContentType { get; set; } = ReportedContentType.Paper;

        /// <summary>The item's id (a paper, exam, question... id; no foreign key, the kinds live in different tables).</summary>
        [Column(nameof(ContentId), DataType.Long)]
        [Required]
        public long ContentId { get; set; }

        /// <summary>The kind of problem the reporter picked (the page's list: 1 can't download, 2 wrong file, 3 bad content...).</summary>
        [Column(nameof(ReportType), DataType.Byte)]
        [Required]
        public byte ReportType { get; set; }

        [Column(nameof(Message), DataType.UnicodeString)]
        [StringLength(MaxMessageLength)]
        [Required]
        public string Message { get; set; } = string.Empty;

        [Column(nameof(UserId), DataType.Long)]
        [Required]
        public long UserId { get; set; }
        public ApplicationUser? User { get; set; }

        [Column(nameof(Status), DataType.Byte)]
        [Required]
        public ContentReportStatus Status { get; set; } = ContentReportStatus.Open;

        /// <summary>Staff's note on what was done.</summary>
        [Column(nameof(AdminNote), DataType.UnicodeString)]
        [StringLength(500)]
        public string? AdminNote { get; set; }

        [Column(nameof(CreationDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset CreationDate { get; set; }

        /// <summary>When it was resolved or dismissed.</summary>
        [Column(nameof(ClosedDate), DataType.DateTimeOffset)]
        public DateTimeOffset? ClosedDate { get; set; }

        [Column(nameof(ClosedByUserId), DataType.Long)]
        public long? ClosedByUserId { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<ContentReport> builder)
        {
            _ = builder.OwnEnumeration<ContentReport, ReportedContentType, byte>(t => t.ContentType);
            _ = builder.OwnEnumeration<ContentReport, ContentReportStatus, byte>(t => t.Status);
            _ = builder.HasIndex(t => new { t.ContentType, t.ContentId });
            _ = builder.HasIndex(t => new { t.UserId, t.ContentType, t.ContentId });

            // The admin dashboard's queue (open reports).
            _ = builder.HasIndex(t => t.CreationDate, "IX_ContentReports_Open_CreationDate").HasFilter("[Status] = 0");
            _ = builder.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        }
    }
}
