namespace GamaEdtech.Data.Dto.School
{
    /// <summary>An AI draft of a school comment: the text and the 8 ratings (1 to 5), named as in a school comment.</summary>
    public sealed class SchoolAiCommentDto
    {
        public required string Comment { get; set; }
        public int ClassesQualityRate { get; set; }
        public int EducationRate { get; set; }
        public int ITTrainingRate { get; set; }
        public int SafetyAndHappinessRate { get; set; }
        public int BehaviorRate { get; set; }
        public int TuitionRatioRate { get; set; }
        public int FacilitiesRate { get; set; }
        public int ArtisticActivitiesRate { get; set; }
    }
}
