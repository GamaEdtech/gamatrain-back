namespace GamaEdtech.Domain.Enumeration
{
    /// <summary>
    /// Stable keys of the seeded <see cref="Entity.Feature"/> rows. The catalog itself is
    /// data-driven; these constants exist only so quota-consuming call sites reference a
    /// compile-time-checked code instead of a string literal. Keep in sync with the Features table.
    /// </summary>
    public static class FeatureCodes
    {
        public const string PastpaperDownload = nameof(PastpaperDownload);
        public const string TestDownload = nameof(TestDownload);
        public const string TestSubmission = nameof(TestSubmission);
        public const string ExamParticipation = nameof(ExamParticipation);
        public const string MultimediaDownload = nameof(MultimediaDownload);
        public const string ExamDownload = nameof(ExamDownload);
    }
}
