namespace GamaEdtech.Data.Dto.Identity
{
    public sealed class DashboardRequestDto
    {
        /// <summary>
        /// Raw legacy JWT to forward to gama-api, straight from the incoming Authorization header (same as
        /// LegacyUpdateGroupRequestDto.Token). Null/empty when the caller has no forwardable legacy token (e.g. a
        /// native/local-token account) - CoreProvider.GetDashboardAsync fails fast in that case rather than
        /// issuing a request gama-api can't authenticate, and IdentityService.GetDashboardAsync treats that as
        /// "legacy data unavailable", not an overall failure.
        /// </summary>
        public string? Token { get; set; }

        /// <summary>
        /// The caller's local ApplicationUser.Group. Selects gama-api's /teachers/dashboard vs /students/dashboard
        /// - mirrors gamatrain-front's own `userType === 5 ? teachers : students` ternary exactly (5 = Teacher;
        /// null or anything else, including 6 = Student, falls through to the student endpoint), so this proxy
        /// changes no behaviour for any caller.
        /// </summary>
        public int? Group { get; set; }
    }
}
