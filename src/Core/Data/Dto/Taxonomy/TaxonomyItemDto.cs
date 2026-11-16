namespace GamaEdtech.Data.Dto.Taxonomy
{
    /// <summary>One item of a <c>types/list</c> answer, with gama-api's fields.</summary>
    public sealed class TaxonomyItemDto
    {
        public int Id { get; set; }
        public short Kind { get; set; }
        public string? Title { get; set; }
        public int ListOrder { get; set; }
        public string? Parent { get; set; }
        public string? Parent2 { get; set; }
        public string? Metadata { get; set; }
        public bool? IsSeason { get; set; }

        /// <summary>A subject's picture file in the <c>SubjectPicture</c> container; null when it has none.</summary>
        public string? Picture { get; set; }
        public bool? IsPaper { get; set; }

        /// <summary>gama-api's <c>bit_delete</c>; its public list never shows a deleted item.</summary>
        public bool IsDeleted { get; set; }
    }
}
