namespace GamaEdtech.Data.Dto.Question
{
    public sealed class OptionDto
    {
        public int Index { get; set; }
        public string? Body { get; set; }
        public bool IsCorrect { get; set; }
    }
}
