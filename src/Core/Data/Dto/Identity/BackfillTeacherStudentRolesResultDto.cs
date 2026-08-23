namespace GamaEdtech.Data.Dto.Identity
{
    /// <summary>
    /// Outcome of a BackfillRoleAndProfileVisibilityFromGroupAsync run - see
    /// IIdentityService.BackfillRoleAndProfileVisibilityFromGroupAsync for what each count means.
    /// </summary>
    public sealed class BackfillTeacherStudentRolesResultDto
    {
        /// <summary>Users with Group = 5 or 6 that were examined.</summary>
        public int TotalCandidates { get; set; }

        /// <summary>Role.Teacher/Role.Student was added or removed for this user (SyncRoleFromGroupAsync actually changed something).</summary>
        public int RoleChanged { get; set; }

        /// <summary>Group = 5 (Teacher) user whose ProfileVisibility was set to Public.</summary>
        public int ProfileFlippedToPublic { get; set; }

        /// <summary>Per-user failure - logged individually, doesn't stop the rest of the batch.</summary>
        public int Failed { get; set; }
    }
}
