namespace GamaEdtech.Data.Dto.Question
{
    public sealed class QuestionDto
    {
        public long Id { get; set; }
        public string? Body { get; set; }
        public IEnumerable<OptionDto> Options { get; set; }
    }
}
