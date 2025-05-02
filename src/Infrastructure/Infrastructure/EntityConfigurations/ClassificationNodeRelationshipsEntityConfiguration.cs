namespace GamaEdtech.Infrastructure.EntityConfigurations
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Domain.Entity;

    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    public class ClassificationNodeRelationshipsEntityConfiguration : IEntityTypeConfiguration<ClassificationNodeRelationship>
    {
        public void Configure([NotNull] EntityTypeBuilder<ClassificationNodeRelationship> builder)
        {
            _ = builder.HasOne(one => one.ClassificationNode)
                .WithMany(many => many.ClassificationNodeRelationships)
                .HasForeignKey(fk => fk.ClassificationNodeId)
                .OnDelete(DeleteBehavior.Restrict);

            _ = builder.Property(prop => prop.NodeRelationEntityType).IsRequired();
            _ = builder.Property(prop => prop.NodeRelationshipEntityId).IsRequired();
        }
    }
}
