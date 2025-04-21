namespace GamaEdtech.Infrastructure.EntityConfigurations
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Domain.Entity;

    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    public class FaqAndFaqCategoryEntityConfiguration : IEntityTypeConfiguration<FaqAndFaqCategory>
    {
        public void Configure([NotNull] EntityTypeBuilder<FaqAndFaqCategory> builder)
        {
            _ = builder.HasOne(one => one.Faq)
               .WithMany(many => many.FAQAndFAQCategories)
               .HasForeignKey(fk => fk.FaqId)
               .OnDelete(DeleteBehavior.Restrict);

            _ = builder.HasOne(one => one.FaqCategory)
                .WithMany(many => many.FAQAndFAQCategories)
                .HasForeignKey(fk => fk.FaqCategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
