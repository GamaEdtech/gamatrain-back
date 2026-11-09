namespace GamaEdtech.Common.Identity
{
    using System;
    using System.Linq;
    using System.Security.Claims;

    using GamaEdtech.Common.Mvc.Routing;

    /// <summary>
    /// The Permission policy's decision, kept out of the startup lambda so it can be tested. A caller passes an endpoint
    /// when they hold one of the roles its <see cref="PermissionAttribute"/> names, or, only on an endpoint marked
    /// <see cref="DelegablePermissionAttribute"/>, a Permission claim whose value is that exact endpoint's name (granted
    /// per user in admin/identities/{userId}/permissions). There is no wildcard: a grant opens one endpoint, never its
    /// controller or area, and never an endpoint that isn't delegable (user/role/token/2FA/money/settings management).
    /// </summary>
    public static class PermissionAuthorization
    {
        public static bool IsAuthorized(ClaimsPrincipal? user, PermissionAttribute? permission, string? endpointName, bool delegable) =>
            user is not null && permission is not null && (HasRole(user, permission) || (delegable && HasGrant(user, endpointName)));

        private static bool HasRole(ClaimsPrincipal user, PermissionAttribute permission) =>
            permission.Roles is not null && Array.Exists(permission.Roles, user.IsInRole);

        private static bool HasGrant(ClaimsPrincipal user, string? endpointName) =>
            !string.IsNullOrEmpty(endpointName)
            && user.Claims.Any(t => t.Type == PermissionConstants.PermissionPolicy && t.Value.Equals(endpointName, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// The stable "Area.Controller.Action" key the frontend matches menu items and pages against (for example
        /// "Admin.Posts.GetPosts"), instead of the long internal endpoint name stored in the claim. Null for an
        /// endpoint outside an area or without a controller action.
        /// </summary>
        public static string? PermissionKey(Endpoint endpoint) =>
            endpoint.Area is null || endpoint.Controller is null || endpoint.Action is null
                ? null
                : $"{endpoint.Area.Value.Key}.{endpoint.Controller.Value.Key}.{endpoint.Action.Value.Key}";
    }
}
