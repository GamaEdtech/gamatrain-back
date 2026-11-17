namespace GamaEdtech.Data.Dto.TopicMatch
{
    /// <summary>What one run of the topic match copy did.</summary>
    public sealed class TopicMatchImportResultDto
    {
        public int Read { get; set; }

        public int Added { get; set; }

        public int Updated { get; set; }

        /// <summary>Matches gone from gama-api (rejected there), removed here too.</summary>
        public int Removed { get; set; }
    }
}
