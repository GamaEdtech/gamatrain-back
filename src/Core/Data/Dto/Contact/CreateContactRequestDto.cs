namespace GamaEdtech.Data.Dto.Contact
{
    public sealed class CreateContactRequestDto
    {
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? Subject { get; set; }
        public string? Body { get; set; }
    }
}
