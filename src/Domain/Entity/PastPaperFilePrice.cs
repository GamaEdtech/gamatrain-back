namespace GamaEdtech.Domain.Entity
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Entities;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Common.DataAnnotation.Schema;

    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    /// <summary>
    /// The base price of a past paper file by its type (gama-api's <c>files_price</c>, mirrored with its ids by the
    /// past paper copy): what <c>PastPaperPricing</c> starts from. A file type with no row here is free, as on gama-api.
    /// Nothing reads it until past paper downloads move here. See docs/business/past-papers.md, "Prices".
    /// </summary>
    [Table(nameof(PastPaperFilePrice))]
    public class PastPaperFilePrice : IEntity<PastPaperFilePrice, long>
    {
        /// <summary>gama-api's <c>files_price.id</c>, kept as it is.</summary>
        [System.ComponentModel.DataAnnotations.Key]
        [Column(nameof(Id), DataType.Long)]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        [Required]
        public long Id { get; set; }

        /// <summary>gama-api's price kind (<c>type</c>): 1 for past paper files.</summary>
        [Column(nameof(CoreType), DataType.Byte)]
        [Required]
        public byte CoreType { get; set; }

        /// <summary>A paper type (<c>test_type_</c>) the row is for; 0 for every type. gama-api's rule does not use it yet.</summary>
        [Column(nameof(CorePaperTypeId), DataType.Int)]
        [Required]
        public int CorePaperTypeId { get; set; }

        /// <summary>The file extensions it prices, comma-separated as gama-api writes them (<c>doc,docx</c>).</summary>
        [Column(nameof(Extensions), DataType.String)]
        [StringLength(50)]
        [Required]
        public string? Extensions { get; set; }

        /// <summary>The base price before pages, referee score and multipliers (<c>price</c>).</summary>
        [Column(nameof(BasePrice), DataType.Int)]
        [Required]
        public int BasePrice { get; set; }

        [Column(nameof(SyncDate), DataType.DateTimeOffset)]
        [Required]
        public DateTimeOffset SyncDate { get; set; }

        public void Configure([NotNull] EntityTypeBuilder<PastPaperFilePrice> builder)
        {
        }
    }
}
