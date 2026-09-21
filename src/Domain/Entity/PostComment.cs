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

    [Table(nameof(PostComment))]
    public class PostComment : VersionableEntity<ApplicationUser, long, long?>, IEntity<PostComment, long>, IPostId, IStatus
    {
        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Long)]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Required]
        public long Id { get; set; }

        [Column(nameof(PostId), DataType.Long)]
        [Required]
        public long PostId { get; set; }
        public Post? Post { get; set; }

        [Column(nameof(Comment), DataType.UnicodeMaxString)]
        public string? Comment { get; set; }

        [Column(nameof(LikeCount), DataType.Int)]
        [Required]
        public int LikeCount { get; set; }

        [Column(nameof(DislikeCount), DataType.Int)]
        [Required]
        public int DislikeCount { get; set; }

        [Column(nameof(Status), DataType.Byte)]
        [Required]
        public Status Status { get; set; } = Status.Confirmed;

        [Column(nameof(RejectionComment), DataType.UnicodeString)]
        [StringLength(300)]
        public string? RejectionComment { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<PostComment> builder)
        {
            _ = builder.OwnEnumeration<PostComment, Status, byte>(t => t.Status);
            _ = builder.HasOne(t => t.Post).WithMany(t => t.PostComments).HasForeignKey(t => t.PostId).OnDelete(DeleteBehavior.NoAction);
            _ = builder.HasIndex(t => new { t.CreationUserId, t.PostId }).IsUnique(true);
            _ = builder.HasIndex(t => new { t.PostId, t.CreationDate });
        }
    }
}
