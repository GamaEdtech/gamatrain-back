namespace GamaEdtech.Data.Dto.Game
{
    public sealed class ExamInformationRequestDto
    {
        public required long ExamId { get; set; }
        public required string? SecretKey { get; set; }

        /// <summary>The caller here, when signed in: the native source reads the caller's own fields (their attempt) by it,
        /// since a source never reads the request itself.</summary>
        public long? UserId { get; set; }
    }
}
