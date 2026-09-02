namespace GamaEdtech.Domain.Enumeration
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>
    /// One value per distinct proactive/scheduled nudge the platform can send - see
    /// docs/business/notifications.md, "Nudge system". Each value needs its own eligibility check
    /// (NudgeService's Is*EligibleAsync methods) and its own NudgeTemplate row; adding a new nudge
    /// (profile-completion or otherwise, e.g. a future "invite teacher to create an exam") means
    /// adding a value here plus its eligibility check - the template itself is admin-editable, no
    /// deploy needed for that part. Deliberately separate from the reactive/transactional email
    /// templates on ApplicationSettingsDto (ticket confirmations, subscription lifecycle, etc.) -
    /// those fire once, immediately, off a specific action; these are evaluated on a recurring
    /// schedule and can resend (see UserNudgeLog).
    /// </summary>
    public sealed class NudgeType : Enumeration<NudgeType, byte>
    {
        [Display]
        public static readonly NudgeType RoleMissing = new(nameof(RoleMissing), 0);

        [Display]
        public static readonly NudgeType AvatarMissing = new(nameof(AvatarMissing), 1);

        [Display]
        public static readonly NudgeType NameMissing = new(nameof(NameMissing), 2);

        [Display]
        public static readonly NudgeType BioMissing = new(nameof(BioMissing), 3);

        [Display]
        public static readonly NudgeType SkillsMissing = new(nameof(SkillsMissing), 4);

        [Display]
        public static readonly NudgeType ExperienceMissing = new(nameof(ExperienceMissing), 5);

        public NudgeType()
        {
        }

        public NudgeType(string name, byte value) : base(name, value)
        {
        }
    }
}
