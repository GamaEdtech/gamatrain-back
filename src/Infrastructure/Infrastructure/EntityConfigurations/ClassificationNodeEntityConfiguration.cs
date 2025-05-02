namespace GamaEdtech.Infrastructure.EntityConfigurations
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Infrastructure.EntityConfigurations.Converters;

    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    public class ClassificationNodeEntityConfiguration : IEntityTypeConfiguration<ClassificationNode>
    {
        public void Configure([NotNull] EntityTypeBuilder<ClassificationNode> builder)
        {
            _ = builder.Property(prop => prop.Title)
                .IsRequired()
                .HasMaxLength(50);


            _ = builder.HasMany(many => many.ClassificationNodeRelationships)
                .WithOne(one => one.ClassificationNode);

            _ = builder.Property(e => e.HierarchyPath)
                .HasConversion(new HierarchyPathConverter());

            _ = builder.Property(prop => prop.Image).IsRequired(false);
        }
    }
}
