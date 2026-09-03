namespace GamaEdtech.Domain.Entity
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAccess.Entities;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.DataAnnotation.Schema;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    /// <summary>
    /// Admin-editable content for one NudgeType - the "robust for future use" part of the nudge system:
    /// adding/editing/disabling a nudge's copy is an admin-panel edit, not a deploy. See
    /// docs/business/notifications.md, "Nudge system". Deliberately a real table, not more flat
    /// properties on ApplicationSettingsDto (which already holds 19 reactive/transactional templates) -
    /// those are a different category of email (fired once, immediately, off a specific action) and are
    /// unaffected by this table's existence.
    /// </summary>
    [Table(nameof(NudgeTemplate))]
    public class NudgeTemplate : IEntity<NudgeTemplate, int>, ICreationDate
    {
        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Int)]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Required]
        public int Id { get; set; }

        [Column(nameof(NudgeType), DataType.Byte)]
        [Required]
        public NudgeType? NudgeType { get; set; }

        [Column(nameof(Subject), DataType.UnicodeString)]
        [StringLength(200)]
        [Required]
        public string? Subject { get; set; }

        /// <summary>Placeholders: [RECEIVER_NAME], [CTA_URL].</summary>
        [Column(nameof(Body), DataType.UnicodeMaxString)]
        [Required]
        public string? Body { get; set; }

        [Column(nameof(CtaLabel), DataType.UnicodeString)]
        [StringLength(100)]
        [Required]
        public string? CtaLabel { get; set; }

        [Column(nameof(CtaUrl), DataType.String)]
        [StringLength(500)]
        [Required]
        public string? CtaUrl { get; set; }

        /// <summary>Lets an admin turn a whole nudge type off (e.g. too spammy, or not converting) without a deploy.</summary>
        [Column(nameof(IsActive), DataType.Boolean)]
        [Required]
        public bool IsActive { get; set; }

        [Column(nameof(CreationDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset CreationDate { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<NudgeTemplate> builder)
        {
            _ = builder.HasIndex(t => t.NudgeType).IsUnique();
            _ = builder.OwnEnumeration<NudgeTemplate, NudgeType, byte>(t => t.NudgeType);
        }
    }
}
