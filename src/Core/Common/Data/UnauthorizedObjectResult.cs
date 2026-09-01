namespace GamaEdtech.Common.Data
{
    using Microsoft.AspNetCore.Mvc;

    /// <summary>
    /// Mirrors OkObjectResult{T} but for the rare, deliberate case an action needs to return a real HTTP 401
    /// (not the usual "always 200, check succeeded/errors in the body" convention - see CLAUDE.md) alongside a
    /// normal ApiResponse{T} body. ApiControllerBase.Unauthorized{T} is the one place that constructs this and
    /// sets StatusCode, mirroring how InternalServerError{T} sets its own status externally rather than baking
    /// it into the type. First use: IdentitiesController.GetDashboard, propagating gama-api's own 401/403
    /// rejection of a caller's forwarded legacy token - see docs/business/identity-and-access.md, "User
    /// dashboard proxy".
    /// </summary>
    public sealed class UnauthorizedObjectResult<T>(object? value) : ObjectResult(value), IActionResult<T>
    {
    }
}
