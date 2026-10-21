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

        /// <summary>Percent off the first payment in a checkout recovery email. Optional: omitted keeps the stored value.</summary>
        [Display]
        [System.ComponentModel.DataAnnotations.Range(1, 90)]
        public int? CheckoutRecoveryDiscountPercent { get; set; }

        /// <summary>Days a checkout recovery offer stays redeemable. Optional: omitted keeps the stored value.</summary>
        [Display]
        [System.ComponentModel.DataAnnotations.Range(1, 30)]
        public int? CheckoutRecoveryOfferDays { get; set; }

        /// <summary>
        /// Teacher invite program: the percent of an invited user's subscription payments a teacher earns. Applies to teachers
        /// offered/joining from now on; a teacher who already joined keeps their locked percent. Optional: omitted keeps the stored value.
        /// </summary>
        [Display]
        [System.ComponentModel.DataAnnotations.Range(0, 100)]
        public decimal? ReferralCommissionPercent { get; set; }

        /// <summary>Days a new teacher's emailed offer percent stays lockable; 0 = never expires. Optional: omitted keeps the stored value.</summary>
        [Display]
        [System.ComponentModel.DataAnnotations.Range(0, 365)]
        public int? ReferralOfferValidDays { get; set; }

        /// <summary>The offer email's subject. Optional: omitted keeps the stored one (or the default). Avoid money wording: it reads as spam.</summary>
        [Display]
        [System.ComponentModel.DataAnnotations.StringLength(150)]
        public string? ReferralOfferEmailSubject { get; set; }

        /// <summary>The offer email sent when a user becomes a teacher. Optional: omitted keeps the stored template (or the default).</summary>
        [Display]
        [RequiredTokens("[PERCENT]", "[JOIN_LINK]")]
        public string? ReferralOfferEmailTemplate { get; set; }
    }
}
