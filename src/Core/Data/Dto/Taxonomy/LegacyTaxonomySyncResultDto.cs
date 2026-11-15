namespace GamaEdtech.Data.Dto.Taxonomy
{
    public sealed class LegacyTaxonomySyncResultDto
    {
        public int Calls { get; set; }
        public int Added { get; set; }
        public int Updated { get; set; }
        public int Deleted { get; set; }
        public int Unchanged { get; set; }
    }
}
