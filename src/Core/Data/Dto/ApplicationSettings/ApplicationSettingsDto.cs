namespace GamaEdtech.Data.Dto.ApplicationSettings
{
    public sealed class ApplicationSettingsDto
    {
        public int GridPageSize { get; set; } = 10;
        public string? DefaultTimeZoneId { get; set; }
        public long SchoolContributionPoints { get; set; }
        public long SchoolImageContributionPoints { get; set; }
        public long SchoolCommentContributionPoints { get; set; }
        public long PostContributionPoints { get; set; }
        public long PostCommentContributionPoints { get; set; }
        public long SchoolIssuesContributionPoints { get; set; }
        public long RemoveSchoolImageContributionPoints { get; set; }
        public long EasterEggBronzePoints { get; set; } = 1000000;
        public long EasterEggSilverPoints { get; set; } = 3000000;
        public long EasterEggGoldPoints { get; set; } = 6000000;
        public long TestTimeCorrectSubmissionPoints { get; set; } = 10;
        public long TestTimeIncorrectSubmissionPoints { get; set; } = 10;
        public long ExamCorrectTestSubmissionPoints { get; set; } = 1000;
        public long ExamIncorrectTestSubmissionPoints { get; set; } = 1000;
        public string? TicketConfirmationEmailTemplate { get; set; } = "Hi [RECEIVER_NAME],<br><br>We received your request<hr><br><br>[BODY]";
        public string? SchoolCommentContributionConfirmationEmailTemplate { get; set; } = "Hi [RECEIVER_NAME],<br><br>your Comment Contribution for [SCHOOL_NAME]([SCHOOL_ID]) has been confirmed<br>[COMMENT]";
        public string? PostCommentContributionConfirmationEmailTemplate { get; set; } = "Hi [RECEIVER_NAME],<br><br>your Comment Contribution for [POST_TITLE]([POST_ID]) has been confirmed<br>[COMMENT]";
        public string? SchoolImageContributionConfirmationEmailTemplate { get; set; } = "Hi [RECEIVER_NAME],<br><br>your Image Contribution for [SCHOOL_NAME]([SCHOOL_ID]) has been confirmed";
        public string? RemoveSchoolImageContributionConfirmationEmailTemplate { get; set; } = "Hi [RECEIVER_NAME],<br><br>your Remove Image Contribution for [SCHOOL_NAME]([SCHOOL_ID]) has been confirmed";
        public string? SchoolContributionConfirmationEmailTemplate { get; set; } = "Hi [RECEIVER_NAME],<br><br>your Contribution for [SCHOOL_NAME]([SCHOOL_ID]) has been confirmed";
        public string? SchoolIssuesContributionConfirmationEmailTemplate { get; set; } = "Hi [RECEIVER_NAME],<br><br>your Contribution for [SCHOOL_NAME]([SCHOOL_ID]) has been confirmed<br>[ISSUES]";
        public string? PostContributionConfirmationEmailTemplate { get; set; } = "Hi [RECEIVER_NAME],<br><br>your Post Contribution has been confirmed<br>[POST_TITLE]([POST_ID])";
        public string? RegistrationEmailTemplate { get; set; } = "Hi [RECEIVER_NAME],<br><br>welcome to Gamatrain";
        public string? SchoolContributionRejectionEmailTemplate { get; set; } = "Hi [RECEIVER_NAME],<br><br>your Contribution for [SCHOOL_NAME]([SCHOOL_ID]) has been rejected<br>[REJECTION_REASON]";
        public string? SchoolImageContributionRejectionEmailTemplate { get; set; } = "Hi [RECEIVER_NAME],<br><br>your Contribution for [SCHOOL_NAME]([SCHOOL_ID]) has been rejected<br>[REJECTION_REASON]";
        public string? InitializeDeletingAccountEmailTemplate { get; set; } = "Hi [RECEIVER_NAME],<br><br>your request has been received and your account will be deleted after 7 days at [DATE]";
        public string? StartDeletingAccountEmailTemplate { get; set; } = "Hi [RECEIVER_NAME],<br><br>deleting your account has been started at [DATE]";
        public string? FinishedDeletingAccountEmailTemplate { get; set; } = "Hi [RECEIVER_NAME],<br><br>your account has been deleted at [DATE]";
        public string? AdminTransactionCreationEmailTemplate { get; set; } = "Hi [RECEIVER_NAME],<br><br>a transaction has been created by admin.<br>[POINTS] Points<br>[DESCRIPTION]<br>Current Balance: [CURRENT_BALANCE]";
        public string? SubscriptionCancelledEmailTemplate { get; set; } = "Hi [RECEIVER_NAME],<br><br>Your [PLAN_TITLE] subscription has been set to cancel - you'll keep full access until [DATE], after which it won't renew. You can resume it any time before then.";
        public string? SubscriptionResumedEmailTemplate { get; set; } = "Hi [RECEIVER_NAME],<br><br>Your [PLAN_TITLE] subscription has been resumed - it will continue to auto-renew starting [DATE].";
        public string? SubscriptionSwitchedEmailTemplate { get; set; } = "Hi [RECEIVER_NAME],<br><br>Your subscription has been switched to [PLAN_TITLE], effective [DATE].";
        public decimal ContentOwnerCommissionPercent { get; set; } = 20;
        public decimal ContentOwnerCommissionPayoutThresholdUsd { get; set; } = 100;
    }
}
