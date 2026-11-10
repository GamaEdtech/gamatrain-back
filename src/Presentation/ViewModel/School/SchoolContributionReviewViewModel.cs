namespace GamaEdtech.Presentation.ViewModel.School
{
    public sealed class SchoolContributionReviewViewModel
    {
        public SchoolContributionViewModel? NewValues { get; set; }
        public SchoolResponseViewModel? OldValues { get; set; }

        /// <summary>The photo submitted with the contribution, if any; added to the school on approval.</summary>
        public string? ImageUri { get; set; }

        /// <summary>Whether the submitted photo becomes the school's default image on approval.</summary>
        public bool IsDefaultImage { get; set; }

        /// <summary>The comment and ratings submitted with the contribution, if any; posted on approval.</summary>
        public SchoolContributionCommentReviewViewModel? Comment { get; set; }
    }
}
