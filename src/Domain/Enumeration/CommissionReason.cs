namespace GamaEdtech.Domain.Enumeration
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>
    /// What kind of event earned a ContentOwnerCommission row - deliberately separate from
    /// ContentSource (which external system served a download), since a future reason (e.g. a blog
    /// publish bonus) may not involve an external content source at all. Only one member exists
    /// today; the download-specific columns on ContentOwnerCommission (ExternalContentId,
    /// ExternalFileType, ExternalExtraId, ContentType, DownloaderUserId) are scoped to this reason
    /// and will need to be split out (e.g. nullable, or a per-reason detail table) once a second
    /// reason is actually built - not attempted speculatively here.
    /// </summary>
    public sealed class CommissionReason : Enumeration<CommissionReason, byte>
    {
        [Display]
        public static readonly CommissionReason ContentDownload = new(nameof(ContentDownload), 0);

        public CommissionReason()
        {
        }

        public CommissionReason(string name, byte value) : base(name, value)
        {
        }
    }
}
