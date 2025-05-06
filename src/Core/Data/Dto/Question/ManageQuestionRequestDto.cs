namespace GamaEdtech.Data.Dto.Question
{
    public sealed class ManageQuestionRequestDto
    {
        public long? Id { get; set; }
        public required string Body { get; set; }
        public required IEnumerable<OptionDto> Options { get; set; }
    }
}
