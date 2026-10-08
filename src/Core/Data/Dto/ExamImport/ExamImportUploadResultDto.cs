namespace GamaEdtech.Data.Dto.ExamImport
{
    using System.Text.Json.Serialization;

    /// <summary>The upload as the MCP tools report it: after <c>submit</c>/<c>retry_failed</c> (started), during it
    /// (<c>submission_status</c>), and after <c>publish_exam</c> or <c>discard_draft</c>.</summary>
    public sealed class ExamImportUploadResultDto
    {
        /// <summary>The import to run the background upload for, when one was just started (not shown to the AI).</summary>
        [JsonIgnore]
        public long? ImportId { get; set; }

        /// <summary>The run to hand to the background upload with <see cref="ImportId"/> (not shown to the AI).</summary>
        [JsonIgnore]
        public string? RunId { get; set; }

        /// <summary>idle, running, waitingRateLimit, interrupted, draftReady, published, failed or cancelled.</summary>
        public string? Phase { get; set; }

        public string? Message { get; set; }

        /// <summary>Questions in this run.</summary>
        public int? Questions { get; set; }

        public int? EstimatedMinutes { get; set; }

        /// <summary>Created out of queued, e.g. <c>12/40</c>.</summary>
        public string? Progress { get; set; }

        public IReadOnlyDictionary<string, ExamImportUploadDto.FailureDto>? Failed { get; set; }

        public long? ExamId { get; set; }

        /// <summary>The draft on gamatrain's exam builder: the owner sees every question there before publishing.</summary>
        public Uri? DraftUrl { get; set; }

        /// <summary>The published exam.</summary>
        public Uri? ExamUrl { get; set; }

        public long? DeletedExam { get; set; }

        public IReadOnlyList<long>? DeletedQuestions { get; set; }

        public IReadOnlyList<string>? Errors { get; set; }
    }
}
