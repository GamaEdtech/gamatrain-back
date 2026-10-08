namespace GamaEdtech.Data.Dto.ExamImport
{
    /// <summary>One of gama-api's choices for an exam detail (a board, grade, course, subject, topic or paper type).</summary>
    public sealed class ExamImportOptionDto
    {
        public int Id { get; set; }

        public string? Title { get; set; }
    }
}
