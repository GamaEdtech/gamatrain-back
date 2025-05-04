namespace GamaEdtech.Infrastructure.EntityConfigurations.Converters
{
    using GamaEdtech.Domain.Valueobjects;

    using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

    public class HierarchyPathConverter : ValueConverter<HierarchyPath, string>
    {
        public HierarchyPathConverter() : base(
                v => v.Value,
                v => new HierarchyPath(v.ToString())
           )
        { }
    }
}
