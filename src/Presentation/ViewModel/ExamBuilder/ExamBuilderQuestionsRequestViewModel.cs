namespace GamaEdtech.Presentation.ViewModel.ExamBuilder
{
    /// <summary>An exam's whole question list, in order.</summary>
    public sealed class ExamBuilderQuestionsRequestViewModel
    {
        public IReadOnlyList<long> QuestionIds { get; set; } = [];
    }
}
