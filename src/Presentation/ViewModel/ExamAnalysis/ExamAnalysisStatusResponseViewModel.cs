namespace GamaEdtech.Presentation.ViewModel.ExamAnalysis
{
    public sealed class ExamAnalysisStatusResponseViewModel
    {
        /// <summary>The saved analysis text, <see langword="null"/> when there is none yet.</summary>
        public string? Result { get; set; }

        /// <summary>The saved analysis's id, <see langword="null"/> when there is none yet.</summary>
        public long? Id { get; set; }

        /// <summary>What asking for the analysis costs this caller now; 0 when it is already paid for.</summary>
        public long Points { get; set; }

        /// <summary>Already paid for, so asking for it is free.</summary>
        public bool Purchased { get; set; }
    }
}
