namespace GamaEdtech.Presentation.ViewModel.Question
{
    public sealed class RandomQuestionResponseViewModel
    {
        public long Id { get; set; }
        public string? Body { get; set; }
        public IEnumerable<OptionInfoViewModel> Options { get; set; }
        public string Values { get; set; }
    }
}
