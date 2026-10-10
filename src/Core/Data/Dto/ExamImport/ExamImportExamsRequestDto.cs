namespace GamaEdtech.Data.Dto.ExamImport
{
    /// <summary>A page of the caller's own exams on gama-api.</summary>
    public sealed class ExamImportExamsRequestDto
    {
        /// <summary>The caller's gama-api token.</summary>
        public required string SecretKey { get; set; }

        /// <summary>The caller's gama-api user id: gama-api's staff see everyone's exams without it.</summary>
        public required long UserId { get; set; }

        /// <summary>gama-api's exam statuses to list; empty for all.</summary>
        public IReadOnlyList<int> Statuses { get; set; } = [];

        public int Page { get; set; } = 1;

        public int PageSize { get; set; }
    }
}
