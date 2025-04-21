namespace GamaEdtech.Infrastructure.EntityConfigurations.Converters
{
    using GamaEdtech.Domain.Valueobjects;

    using Microsoft.EntityFrameworkCore;
    using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

    public class HierarchyPathConverter : ValueConverter<HierarchyPath, HierarchyId>
    {
        public HierarchyPathConverter() : base(
                v => HierarchyId.Parse(v.Value),
                v => new HierarchyPath(v.ToString())
           )
        { }
    }
}
