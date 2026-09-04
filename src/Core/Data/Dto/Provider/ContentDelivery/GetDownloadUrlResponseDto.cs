namespace GamaEdtech.Data.Dto.Provider.ContentDelivery
{
    public sealed class GetDownloadUrlResponseDto
    {
        /// <summary>Null exactly when <see cref="LegacyAuthRejected"/> is true - otherwise always set on a Succeeded result.</summary>
        public string? Url { get; set; }
        public string? Name { get; set; }

        /// <summary>The content owner's id in the source system (e.g. gama-api's CoreId), when the source reports one - only /tests/download does. Null means no commission can be accrued.</summary>
        public long? OwnerExternalId { get; set; }

        /// <summary>The source's own reported price for this content, in points, when the source reports one - only /tests/download does. Null means no charge applies at all.</summary>
        public long? Points { get; set; }

        /// <summary>Whether the source considers this specific download already paid for - null when the source doesn't report pricing at all (Multimedia/Exam), true/false only for priced content (PastPaper/Test).</summary>
        public bool? Paid { get; set; }

        /// <summary>
        /// gama-api rejected the caller's own forwarded legacy token (401/403) - the session may still be
        /// cryptographically valid to this backend's own auth but is no longer honored on gama-api's side (e.g.
        /// ended via gama-api's own logout, or between this call and an earlier one in the same request that did
        /// still succeed). Deliberately reported as a Succeeded result with this flag set, not Failed - same
        /// convention as <c>LegacyDashboardDataDto.LegacyAuthRejected</c> - gama-api answered and gave a real,
        /// understood answer, this just isn't the URL. <see cref="Url"/> is null exactly when this is true.
        /// </summary>
        public bool LegacyAuthRejected { get; set; }
    }
}
