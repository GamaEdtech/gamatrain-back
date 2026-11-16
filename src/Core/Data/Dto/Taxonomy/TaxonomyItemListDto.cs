namespace GamaEdtech.Data.Dto.Taxonomy
{
    /// <summary>One page of a lookup list for the admin panel.</summary>
    public sealed class TaxonomyItemListDto
    {
        public IReadOnlyList<TaxonomyItemDto> Items { get; set; } = [];

        public int Total { get; set; }
    }
}
