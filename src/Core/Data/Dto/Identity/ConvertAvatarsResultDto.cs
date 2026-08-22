namespace GamaEdtech.Data.Dto.Identity
{
    /// <summary>Outcome of a ConvertAvatarsAsync run - see IIdentityService.ConvertAvatarsAsync for what each count means.</summary>
    public sealed class ConvertAvatarsResultDto
    {
        /// <summary>Legacy base64 Avatar successfully written to a file and AvatarId set.</summary>
        public int Converted { get; set; }

        /// <summary>Avatar value didn't match the expected "data:image/*;base64,*" shape - left untouched, not counted as a failure.</summary>
        public int Skipped { get; set; }

        /// <summary>Matched the expected shape but conversion/save/update failed (bad base64, file-write error, etc.) - logged individually, doesn't stop the rest of the batch.</summary>
        public int Failed { get; set; }
    }
}
