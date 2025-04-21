namespace GamaEdtech.Infrastructure.EntityConfigurations
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Infrastructure.EntityConfigurations.Converters;

    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    public class FaqCategoryEntityConfiguration : IEntityTypeConfiguration<FaqCategory>
    {
        public void Configure([NotNull] EntityTypeBuilder<FaqCategory> builder)
        {
            _ = builder.Property(prop => prop.Title)
                .IsRequired()
                .HasMaxLength(50);

            _ = builder.HasMany(many => many.FAQAndFAQCategories)
                .WithOne(one => one.FaqCategory);

            _ = builder.Property(e => e.HierarchyPath)
                .HasConversion(new HierarchyPathConverter());
        }
    }
}
