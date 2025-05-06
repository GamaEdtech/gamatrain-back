namespace GamaEdtech.Presentation.ViewModel.Contact
{
    public sealed class ContactResponseViewModel
    {
        public long Id { get; set; }
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? Subject { get; set; }
        public string? Body { get; set; }
    }
}
