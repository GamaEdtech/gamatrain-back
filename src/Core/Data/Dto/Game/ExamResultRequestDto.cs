namespace GamaEdtech.Data.Dto.Game
{
    public sealed class ExamResultRequestDto
    {
        public required long ExamId { get; set; }
        public required string? SecretKey { get; set; }

        /// <summary>The caller here; the native source answers only their own result (or one of their exam's).</summary>
        public long? UserId { get; set; }
    }
}
