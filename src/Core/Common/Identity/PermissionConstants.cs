namespace GamaEdtech.Common.Identity
{
    public static class PermissionConstants
    {
        public const string SystemClaim = "SystemClaim";
        public const string PermissionPolicy = "Permission";
        public const string ApiKeyPolicy = "ApiKey";

        public static readonly string ApiDataProtectorTokenProvider = "ApiDataProtectorTokenProvider";
        public static readonly string ApiDataProtectorTokenProviderAccessToken = "ApiDataProtectorTokenProviderAccessToken";

        public const string TokenAuthenticationScheme = "TokenAuthenticationScheme";
        internal const string ApiKeyAuthenticationScheme = "ApiKeyAuthenticationScheme";
    }
}
