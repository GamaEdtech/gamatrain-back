namespace GamaEdtech.Data.Dto.ExamImport
{
    /// <summary>
    /// The upload progress of an import (<c>ExamImport.Upload</c>), written by the background upload after every step so
    /// a restart resumes where it stopped without creating a question twice.
    /// </summary>
    public sealed class ExamImportUploadDto
    {
        /// <summary>idle, running, waitingRateLimit, draftReady, published, failed or cancelled.</summary>
        public string Phase { get; set; } = "idle";

        /// <summary>The current background run; a run that finds another id here stops (discarded, or a newer run).</summary>
        public string? RunId { get; set; }

        /// <summary>The draft exam on gama-api (status 6 until published).</summary>
        public long? ExamId { get; set; }

        public string? ExamCode { get; set; }

        /// <summary>Question number to gama-api's examTests id, for the questions already created.</summary>
        public Dictionary<string, long> Created { get; set; } = [];

        /// <summary>Question number (or <c>_job</c> for the whole upload) to why it failed.</summary>
        public Dictionary<string, FailureDto> Failed { get; set; } = [];

        /// <summary>The question numbers this run uploads, in paper order.</summary>
        public IReadOnlyList<string> Queue { get; set; } = [];

        public string? Message { get; set; }

        public DateTimeOffset? StartedAt { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }

        public sealed class FailureDto
        {
            /// <summary>gama-api's error code, e.g. <c>requiredField</c>.</summary>
            public string? Error { get; set; }

            public string? Message { get; set; }
        }
    }
}
