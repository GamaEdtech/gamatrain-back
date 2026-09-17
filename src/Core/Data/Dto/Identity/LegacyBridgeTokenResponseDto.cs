namespace GamaEdtech.Data.Dto.Identity
{
    public sealed class LegacyBridgeTokenResponseDto
    {
        /// <summary>
        /// Set (e.g. "loginByOTP") when gama-api requires another step instead of returning a token - in that
        /// case UserId/Token/ExpirationTime are all unset. Null on a normal successful auth.
        /// </summary>
        public string? Type { get; set; }
        public long UserId { get; set; }
        public string? Token { get; set; }
        public DateTimeOffset? ExpirationTime { get; set; }

        /// <summary>
        /// True when this call just auto-created the local ApplicationUser mirror (SyncLegacyAuthAsync's "user is
        /// null" branch) rather than syncing an existing one. Internal signal only, never mapped onto the public
        /// LegacyAuthTokenResponseViewModel - lets the controller decide whether to send the registration email,
        /// same as the plain IdentitiesController.Register flow does on its own user-creation success.
        /// </summary>
        public bool IsNewUser { get; set; }
        public string? Email { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
    }
}
