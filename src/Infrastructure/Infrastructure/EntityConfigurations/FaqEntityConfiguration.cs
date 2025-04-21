namespace GamaEdtech.Infrastructure.EntityConfigurations
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Domain.Entity;

    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    public class FaqEntityConfiguration : IEntityTypeConfiguration<Faq>
    {
        public void Configure([NotNull] EntityTypeBuilder<Faq> builder)
        {
            _ = builder.Property(prop => prop.SummaryOfQuestion)
                .IsRequired(false);

            _ = builder.Property(prop => prop.Question)
                .IsRequired(false);

            _ = builder.HasMany(one => one.FAQAndFAQCategories)
                .WithOne(many => many.Faq);
        }
    }
}
