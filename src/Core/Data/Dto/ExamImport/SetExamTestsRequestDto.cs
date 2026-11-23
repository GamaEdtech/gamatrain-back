namespace GamaEdtech.Data.Dto.ExamImport
{
    /// <summary>The whole question list of an exam on gama-api, in exam order.</summary>
    public sealed class SetExamTestsRequestDto
    {
        /// <summary>The caller's gama-api token.</summary>
        public required string SecretKey { get; set; }

        /// <summary>The signed-in caller here (from the MCP token), which the native source acts for.</summary>
        public long UserId { get; set; }

        public long ExamId { get; set; }

        public IEnumerable<long> TestIds { get; set; } = [];
    }
}
