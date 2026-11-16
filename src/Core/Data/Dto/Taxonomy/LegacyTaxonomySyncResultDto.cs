namespace GamaEdtech.Data.Dto.Taxonomy
{
    public sealed class LegacyTaxonomySyncResultDto
    {
        /// <summary>Batches read from gama-api's database.</summary>
        public int Calls { get; set; }
        public int Added { get; set; }
        public int Updated { get; set; }
        public int Deleted { get; set; }
        public int Unchanged { get; set; }

        /// <summary>gama-api lookup items read (of the copied lists).</summary>
        public int Read { get; set; }
    }
}
