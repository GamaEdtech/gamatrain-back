namespace GamaEdtech.Data.Dto.Migration
{
    /// <summary>What the shadow comparison of one domain and feature has seen since this process started.</summary>
    public sealed class MigrationShadowStatisticsDto
    {
        /// <summary>The domain's name, then <c>.</c> and the feature when the call named one.</summary>
        public required string Key { get; init; }

        /// <summary>Calls where the native source answered the same as gama-api.</summary>
        public long Matched { get; init; }

        /// <summary>Calls where the two answers differed.</summary>
        public long Mismatched { get; init; }

        /// <summary>Calls where the native source threw.</summary>
        public long Failed { get; init; }

        /// <summary>Calls not compared because too many comparisons were already running.</summary>
        public long Skipped { get; init; }
    }
}
