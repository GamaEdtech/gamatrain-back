namespace GamaEdtech.Data.Dto.Taxonomy
{
    /// <summary>gama-api's <c>types/list</c> query: the list's name and the parent filter that list takes.</summary>
    public sealed class TaxonomyListRequestDto
    {
        /// <summary>A key of <see cref="LegacyTaxonomyKinds.ByListName"/>.</summary>
        public required string Type { get; set; }

        /// <summary>The board, for <c>base</c>, <c>course</c> and <c>test_type</c>.</summary>
        public long? SectionId { get; set; }

        /// <summary>The grade, for <c>lesson</c>.</summary>
        public long? BaseId { get; set; }

        /// <summary>The course, for <c>lesson</c> (0 or none: subjects outside any course).</summary>
        public long? CourseId { get; set; }

        /// <summary>The subject, for <c>topic</c>.</summary>
        public long? LessonId { get; set; }
    }
}
