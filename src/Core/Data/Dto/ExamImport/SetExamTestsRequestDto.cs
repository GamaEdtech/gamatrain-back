namespace GamaEdtech.Data.Dto.ExamImport
{
    /// <summary>The whole question list of an exam on gama-api, in exam order.</summary>
    public sealed class SetExamTestsRequestDto
    {
        /// <summary>The caller's gama-api token.</summary>
        public required string SecretKey { get; set; }

        public long ExamId { get; set; }

        public IEnumerable<long> TestIds { get; set; } = [];
    }
}
