namespace GamaEdtech.Domain.Enumeration
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>A tutorial's review status. The values are gama-api's own <c>dars.status</c> codes (only 0 and 1 are in use
    /// there), so a copied row keeps its meaning; the others follow the past papers' codes for the admin review later.</summary>
    public sealed class TutorialStatus : Enumeration<TutorialStatus, byte>
    {
        [Display]
        public static readonly TutorialStatus Pending = new(nameof(Pending), 0);

        [Display]
        public static readonly TutorialStatus Confirmed = new(nameof(Confirmed), 1);

        [Display]
        public static readonly TutorialStatus Rejected = new(nameof(Rejected), 2);

        [Display]
        public static readonly TutorialStatus NeedsChanges = new(nameof(NeedsChanges), 3);

        [Display]
        public static readonly TutorialStatus Deactivated = new(nameof(Deactivated), 4);

        public TutorialStatus()
        {
        }

        public TutorialStatus(string name, byte value) : base(name, value)
        {
        }
    }
}
