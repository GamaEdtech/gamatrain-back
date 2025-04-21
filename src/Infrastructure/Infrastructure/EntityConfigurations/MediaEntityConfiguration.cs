namespace GamaEdtech.Infrastructure.EntityConfigurations
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Domain.Entity;

    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    public class MediaEntityConfiguration : IEntityTypeConfiguration<Media>
    {
        public void Configure([NotNull] EntityTypeBuilder<Media> builder)
        {
            _ = builder.Property(m => m.FileName)
                .IsRequired()
                .HasMaxLength(500);

            _ = builder.Property(m => m.ContentType)
                .IsRequired()
                .HasMaxLength(100);

            _ = builder.Property(m => m.FileAddress)
                .IsRequired()
                .HasMaxLength(500);

            _ = builder.HasIndex(m => new { m.MediaEntity, m.MediaEntityId });
        }
    }
}
