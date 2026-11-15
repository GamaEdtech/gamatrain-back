namespace GamaEdtech.Domain.Enumeration
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>A past paper's review status. The values are gama-api's own <c>tests.status</c> codes, so a copied row
    /// keeps its meaning.</summary>
    public sealed class PastPaperStatus : Enumeration<PastPaperStatus, byte>
    {
        [Display]
        public static readonly PastPaperStatus Pending = new(nameof(Pending), 0);

        [Display]
        public static readonly PastPaperStatus Confirmed = new(nameof(Confirmed), 1);

        [Display]
        public static readonly PastPaperStatus Rejected = new(nameof(Rejected), 2);

        /// <summary>Sent back to the owner with a review note.</summary>
        [Display]
        public static readonly PastPaperStatus NeedsChanges = new(nameof(NeedsChanges), 3);

        [Display]
        public static readonly PastPaperStatus Deactivated = new(nameof(Deactivated), 4);

        /// <summary>Soft-deleted by an admin; still shown to the owner and managers.</summary>
        [Display]
        public static readonly PastPaperStatus Deleted = new(nameof(Deleted), 5);

        /// <summary>Edited by the owner after review, waiting for review again.</summary>
        [Display]
        public static readonly PastPaperStatus Edited = new(nameof(Edited), 6);

        public PastPaperStatus()
        {
        }

        public PastPaperStatus(string name, byte value) : base(name, value)
        {
        }
    }
}
