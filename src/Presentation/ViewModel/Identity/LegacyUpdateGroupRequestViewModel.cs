namespace GamaEdtech.Presentation.ViewModel.Identity
{
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>
    /// Group is the same teacher/student signal as ApplicationUser.Group - 5 = Teacher, 6 = Student. See
    /// ApplicationUser.Group's doc comment / docs/business/identity-and-access.md. Not the same concept as the
    /// Teacher/Student values on Role. gama-api's own "uid" form field is deliberately not exposed here - see
    /// ICoreProvider.LegacyUpdateGroupAsync.
    /// </summary>
    public sealed class LegacyUpdateGroupRequestViewModel
    {
        [Display]
        [Required]
        public int Group { get; set; }
    }
}
