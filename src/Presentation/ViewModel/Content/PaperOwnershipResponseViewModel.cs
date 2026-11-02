namespace GamaEdtech.Presentation.ViewModel.Content
{
    /// <summary>Which of a past paper's files the caller already owns: downloading one of these again costs nothing.</summary>
    public sealed class PaperOwnershipResponseViewModel
    {
        public bool Pdf { get; set; }

        public bool Word { get; set; }

        public bool Answer { get; set; }
    }
}
