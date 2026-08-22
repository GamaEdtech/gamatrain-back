namespace GamaEdtech.Presentation.ViewModel.Identity
{
    public sealed class ConvertAvatarsResponseViewModel
    {
        /// <summary>Legacy base64 Avatar successfully written to a file and AvatarId set.</summary>
        public int Converted { get; set; }

        /// <summary>Avatar value didn't match the expected "data:image/*;base64,*" shape, or was already handled by a concurrent run - left untouched, not a failure.</summary>
        public int Skipped { get; set; }

        /// <summary>Matched the expected shape but conversion/save failed - see server logs for the per-user reason. Safe to re-run; only these (and any not-yet-converted) rows are picked up again.</summary>
        public int Failed { get; set; }
    }
}
