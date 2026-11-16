namespace GamaEdtech.Data.Dto.Taxonomy
{
    /// <summary>A lookup item an admin adds or edits (gama-api's <c>type_slave</c> columns).</summary>
    public sealed class TaxonomyItemEditDto
    {
        /// <summary>The list (gama-api's <c>master_</c>): one of <see cref="LegacyTaxonomyKinds.ByListName"/>'s values.</summary>
        public short Kind { get; set; }

        public string? Title { get; set; }

        public int ListOrder { get; set; }

        /// <summary>The parents, "id+id" (a subject's are "course,grade").</summary>
        public string? Parent { get; set; }

        public string? Parent2 { get; set; }

        /// <summary>A JSON object (<c>is_paper</c>, <c>season</c>, <c>price_multiplier</c>...), or none.</summary>
        public string? Metadata { get; set; }
    }
}
