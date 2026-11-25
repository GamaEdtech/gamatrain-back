namespace GamaEdtech.Presentation.ViewModel.Content
{
    /// <summary>Which of a past paper's files the caller already owns: downloading one of these again costs nothing.</summary>
    public sealed class PaperOwnershipResponseViewModel
    {
        /// <summary>The owned files' ids, as the paper's <c>files</c> list names them (since 2026-11-25).</summary>
        public IReadOnlyList<long> FileIds { get; set; } = [];
    }
}
