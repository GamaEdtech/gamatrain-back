namespace GamaEdtech.Presentation.ViewModel.School
{
    public sealed class SchoolMemberRoleViewModel
    {
        public required string Name { get; set; }

        public byte Value { get; set; }

        public string? Title { get; set; }

        /// <summary>The member can choose to be named on the school page (teachers and staff).</summary>
        public bool CanShowName { get; set; }
    }
}
