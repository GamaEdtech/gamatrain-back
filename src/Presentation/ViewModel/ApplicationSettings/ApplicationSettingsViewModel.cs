namespace GamaEdtech.Presentation.ViewModel.ApplicationSettings
{
    using GamaEdtech.Common.DataAnnotation;

    public sealed class ApplicationSettingsViewModel
    {
        [Display]
        [Required]
        public int? GridPageSize { get; set; }

        [Display]
        [Required]
        [TimeZoneId]
        public string? DefaultTimeZoneId { get; set; }

        [Display]
        [Required]
        public long? SchoolContributionPoints { get; set; }

        [Display]
        [Required]
        public long? SchoolImageContributionPoints { get; set; }

        [Display]
        [Required]
        public long? SchoolCommentContributionPoints { get; set; }

        [Display]
        [Required]
        public long? PostContributionPoints { get; set; }

        [Display]
        [Required]
        public long? SchoolIssuesContributionPoints { get; set; }

        [Display]
        [Required]
        public long? RemoveSchoolImageContributionPoints { get; set; }

        [Display]
        [Required]
        public long? EasterEggBronzePoints { get; set; }

        [Display]
        [Required]
        public long? EasterEggSilverPoints { get; set; }

        [Display]
        [Required]
        public long? EasterEggGoldPoints { get; set; }

        [Display]
        [Required]
        public long? TestTimeCorrectSubmissionPoints { get; set; }

        [Display]
        [Required]
        public long? TestTimeIncorrectSubmissionPoints { get; set; }

        [Display]
        [Required]
        public long? ExamCorrectTestSubmissionPoints { get; set; }

        [Display]
        [Required]
        public long? ExamIncorrectTestSubmissionPoints { get; set; }

        [Display]
        [Required]
        [RequiredTokens("[RECEIVER_NAME]", "[SCHOOL_NAME]", "[SCHOOL_ID]", "[COMMENT]")]
        public string? SchoolCommentContributionConfirmationEmailTemplate { get; set; }

        [Display]
        [Required]
        [RequiredTokens("[RECEIVER_NAME]", "[POST_TITLE]", "[POST_ID]", "[COMMENT]")]
        public string? PostCommentConfirmationEmailTemplate { get; set; }

        [Display]
        [Required]
        [RequiredTokens("[RECEIVER_NAME]", "[SCHOOL_NAME]", "[SCHOOL_ID]")]
        public string? SchoolImageContributionConfirmationEmailTemplate { get; set; }

        [Display]
        [Required]
        [RequiredTokens("[RECEIVER_NAME]", "[SCHOOL_NAME]", "[SCHOOL_ID]")]
        public string? RemoveSchoolImageContributionConfirmationEmailTemplate { get; set; }

        [Display]
        [Required]
        [RequiredTokens("[RECEIVER_NAME]", "[SCHOOL_NAME]", "[SCHOOL_ID]")]
        public string? SchoolContributionConfirmationEmailTemplate { get; set; }

        [Display]
        [Required]
        [RequiredTokens("[RECEIVER_NAME]", "[SCHOOL_NAME]", "[SCHOOL_ID]", "[ISSUES]")]
        public string? SchoolIssuesContributionConfirmationEmailTemplate { get; set; }

        [Display]
        [Required]
        [RequiredTokens("[RECEIVER_NAME]", "[POST_TITLE]", "[POST_ID]")]
        public string? PostConfirmationEmailTemplate { get; set; }

        [Display]
        [Required]
        [RequiredTokens("[RECEIVER_NAME]", "[BODY]")]
        public string? TicketConfirmationEmailTemplate { get; set; }

        [Display]
        [Required]
        [RequiredTokens("[RECEIVER_NAME]")]
        public string? RegistrationEmailTemplate { get; set; }

        [Display]
        [Required]
        [RequiredTokens("[RECEIVER_NAME]", "[SCHOOL_NAME]", "[SCHOOL_ID]", "[REJECTION_REASON]")]
        public string? SchoolContributionRejectionEmailTemplate { get; set; }

        [Display]
        [Required]
        [RequiredTokens("[RECEIVER_NAME]", "[SCHOOL_NAME]", "[SCHOOL_ID]", "[REJECTION_REASON]")]
        public string? SchoolImageContributionRejectionEmailTemplate { get; set; }

        [Display]
        [Required]
        [RequiredTokens("[RECEIVER_NAME]", "[DATE]")]
        public string? InitializeDeletingAccountEmailTemplate { get; set; }

        [Display]
        [Required]
        [RequiredTokens("[RECEIVER_NAME]", "[DATE]")]
        public string? StartDeletingAccountEmailTemplate { get; set; }

        [Display]
        [Required]
        [RequiredTokens("[RECEIVER_NAME]", "[DATE]")]
        public string? FinishedDeletingAccountEmailTemplate { get; set; }

        [Display]
        [Required]
        [RequiredTokens("[RECEIVER_NAME]", "[DESCRIPTION]", "[POINTS]", "[CURRENT_BALANCE]")]
        public string? AdminTransactionCreationEmailTemplate { get; set; }

        [Display]
        [Required]
        [RequiredTokens("[RECEIVER_NAME]", "[PLAN_TITLE]", "[DATE]")]
        public string? SubscriptionCancelledEmailTemplate { get; set; }

        [Display]
        [Required]
        [RequiredTokens("[RECEIVER_NAME]", "[PLAN_TITLE]", "[DATE]")]
        public string? SubscriptionResumedEmailTemplate { get; set; }

        [Display]
        [Required]
        [RequiredTokens("[RECEIVER_NAME]", "[PLAN_TITLE]", "[DATE]")]
        public string? SubscriptionSwitchedEmailTemplate { get; set; }

        [Display]
        [Required]
        public decimal? ContentOwnerCommissionPercent { get; set; }

        /// <summary>The minimum payout request, in USD. Can't be set below $100.</summary>
        [Display]
        [Required]
        [Range(100, 1_000_000)]
        public decimal? ContentOwnerCommissionPayoutThresholdUsd { get; set; }

        /// <summary>Optional: omitted keeps the stored template (or the default).</summary>
        [Display]
        [RequiredTokens("[RECEIVER_NAME]", "[AMOUNT]")]
        public string? CommissionPayoutRequestedEmailTemplate { get; set; }

        /// <summary>Optional: omitted keeps the stored template (or the default).</summary>
        [Display]
        [RequiredTokens("[RECEIVER_NAME]", "[AMOUNT]", "[TRANSFER_REFERENCE]")]
        public string? CommissionPayoutPaidEmailTemplate { get; set; }

        /// <summary>Optional: omitted keeps the stored template (or the default). Must contain [CODE].</summary>
        [Display]
        [RequiredTokens("[CODE]")]
        public string? TwoFactorSetupEmailTemplate { get; set; }

        /// <summary>Exam export price = question count x this, per format. Optional: omitted keeps the stored value.</summary>
        [Display]
        [System.ComponentModel.DataAnnotations.Range(0, 1000)]
        public decimal? ExamExportPdfMultiplier { get; set; }

        [Display]
        [System.ComponentModel.DataAnnotations.Range(0, 1000)]
        public decimal? ExamExportWordMultiplier { get; set; }

        [Display]
        [System.ComponentModel.DataAnnotations.Range(0, 1000)]
        public decimal? ExamExportPowerPointMultiplier { get; set; }
    }
}
