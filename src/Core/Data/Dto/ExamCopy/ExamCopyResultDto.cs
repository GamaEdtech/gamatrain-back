namespace GamaEdtech.Data.Dto.ExamCopy
{
    /// <summary>What one run of the exam copy did, per table.</summary>
    public sealed class ExamCopyResultDto
    {
        public ExamCopyTableResultDto Exams { get; set; } = new();

        public ExamCopyTableResultDto Questions { get; set; } = new();

        public ExamCopyTableResultDto Attempts { get; set; } = new();

        public ExamCopyTableResultDto AnswerSheets { get; set; } = new();
    }

    public sealed class ExamCopyTableResultDto
    {
        /// <summary>Rows gama-api has.</summary>
        public int Total { get; set; }

        public int Added { get; set; }

        public int Updated { get; set; }

        /// <summary>Rows gone from gama-api, removed here too.</summary>
        public int Removed { get; set; }

        public override string ToString() => $"{Total} total, {Added} added, {Updated} updated, {Removed} removed";
    }
}
