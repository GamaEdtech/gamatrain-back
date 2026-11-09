namespace GamaEdtech.Common.Identity
{
    using System;

    /// <summary>
    /// Marks an admin endpoint (or a whole controller) that a non-admin can be granted one by one in
    /// admin/identities/{userId}/permissions. A per-endpoint grant opens only endpoints carrying this attribute; every
    /// other role-gated endpoint stays open to its roles alone, whatever claims a user holds. Endpoints that manage
    /// users, roles, tokens, passwords, 2FA, money or settings must never carry it: a grant there lets the grantee make
    /// themselves Admin or act as another user.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
    public sealed class DelegablePermissionAttribute : Attribute
    {
    }
}
