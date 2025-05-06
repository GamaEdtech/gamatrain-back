namespace GamaEdtech.Presentation.ViewModel.Question
{
    public sealed class QuestionResponseViewModel
    {
        public long Id { get; set; }
        public string? Body { get; set; }
        public IEnumerable<OptionViewModel> Options { get; set; }
        public string? Values { get; set; }
    }
}
