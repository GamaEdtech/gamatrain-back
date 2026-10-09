namespace GamaEdtech.Data.Dto.ExamImport
{
    /// <summary>An image uploaded to gama-api for a question.</summary>
    public sealed class ExamImportFigureDto
    {
        /// <summary>gama-api's upload key, for a question's <c>figure</c>, <c>optionFigures</c> or <c>answerFigure</c>. It
        /// works for one question only: gama-api moves the file into the question it is saved with.</summary>
        public string? Figure { get; set; }

        public string? Name { get; set; }

        public string? ContentType { get; set; }

        public int Size { get; set; }
    }
}
