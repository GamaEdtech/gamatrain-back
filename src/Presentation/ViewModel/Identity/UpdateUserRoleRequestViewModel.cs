namespace GamaEdtech.Presentation.ViewModel.Identity
{
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>The caller's own user type: <c>Teacher</c> or <c>Student</c> (case-insensitive); anything else is refused.</summary>
    public sealed class UpdateUserRoleRequestViewModel
    {
        [Display]
        [Required]
        public string? Role { get; set; }
    }
}
