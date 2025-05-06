namespace GamaEdtech.Data.Dto.Contact
{
    public sealed class ContactsDto
    {
        public long Id { get; set; }
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? Subject { get; set; }
        public bool IsRead { get; set; }
    }
}
