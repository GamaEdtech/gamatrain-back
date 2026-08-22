namespace GamaEdtech.Presentation.Api
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Domain.Enumeration;

    using Hangfire.Dashboard;

    using Microsoft.AspNetCore.Authentication;
    using Microsoft.AspNetCore.Identity;

    /// <summary>
    /// Gates /hangfire to authenticated Admins only. UseHangfireDashboard() previously ran with no
    /// DashboardOptions at all, so Hangfire fell back to its own default,
    /// LocalRequestsOnlyAuthorizationFilter - which is meaningless behind this app's reverse-proxy topology
    /// (nginx forwarding to http://127.0.0.1:5000): every request Kestrel sees arrives from 127.0.0.1
    /// regardless of the real external client, so "local requests only" effectively allowed everyone. Confirmed
    /// live (2026-08-22): the dashboard was fully reachable, no login, from the public internet on both
    /// production and the sandbox - full job history and the ability to trigger/requeue any job, open to
    /// anyone who found the URL.
    /// Explicitly re-authenticates against the Identity cookie scheme, the realistic path for a human
    /// browsing to this URL in a browser, rather than trusting HttpContext.User to already be correctly
    /// populated by whichever scheme happens to be ambient/default for this request.
    /// </summary>
    public sealed class HangfireDashboardAuthorizationFilter : IDashboardAsyncAuthorizationFilter
    {
        public async Task<bool> AuthorizeAsync([NotNull] DashboardContext context)
        {
            var httpContext = context.GetHttpContext();
            var result = await httpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
            return result.Succeeded && result.Principal?.IsInRole(nameof(Role.Admin)) is true;
        }
    }
}
