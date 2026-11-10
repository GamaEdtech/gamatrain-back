namespace GamaEdtech.Presentation.ViewModel.School
{
    /// <summary>The review a contributor submitted together with a new school (created when the school is approved).</summary>
    public sealed class SchoolContributionCommentReviewViewModel
    {
        public string? Comment { get; set; }
        public double ArtisticActivitiesRate { get; set; }
        public double BehaviorRate { get; set; }
        public double ClassesQualityRate { get; set; }
        public double EducationRate { get; set; }
        public double FacilitiesRate { get; set; }
        public double ITTrainingRate { get; set; }
        public double SafetyAndHappinessRate { get; set; }
        public double TuitionRatioRate { get; set; }
        public double AverageRate { get; set; }
    }
}
