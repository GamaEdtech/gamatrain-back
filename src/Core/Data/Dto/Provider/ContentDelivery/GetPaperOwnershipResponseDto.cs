namespace GamaEdtech.Data.Dto.Provider.ContentDelivery
{
    /// <summary>
    /// Which of a past paper's files the caller already owns, by gama-api's own per-user <c>paid</c> flag on its
    /// side-effect-free <c>GET /tests/{id}</c> (the same flag the download flow trusts before charging). gama-api reports
    /// only the pdf/word/answer files there, not the extra files.
    /// </summary>
    public sealed class GetPaperOwnershipResponseDto
    {
        public bool Pdf { get; set; }

        public bool Word { get; set; }

        public bool Answer { get; set; }

        /// <summary>The ids of the files the caller owns (<c>PastPaperFiles.Id</c>), since 2026-11-25 what the API answers. gama-api
        /// reports <see cref="Pdf"/>/<see cref="Word"/>/<see cref="Answer"/>, which are turned into ids here.</summary>
        public IReadOnlyList<long> FileIds { get; set; } = [];

        /// <summary>gama-api rejected the caller's legacy token (401/403); the flags are meaningless then.</summary>
        public bool LegacyAuthRejected { get; set; }
    }
}
