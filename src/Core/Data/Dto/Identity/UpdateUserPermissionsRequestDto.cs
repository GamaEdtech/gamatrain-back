namespace GamaEdtech.Data.Dto.Identity
{
    using System.Collections.Generic;

    using GamaEdtech.Domain.Enumeration;

    public sealed class UpdateUserPermissionsRequestDto
    {
        public long UserId { get; set; }
        public required IEnumerable<string?> Permissions { get; set; }
        public SystemClaim? SystemClaims { get; set; }
        public Role? Roles { get; set; }

        /// <summary>The admin making the change, whose authenticator code is checked.</summary>
        public long AdminUserId { get; set; }

        /// <summary>The acting admin's current authenticator code (step-up).</summary>
        public string? TwoFactorCode { get; set; }
    }
}
