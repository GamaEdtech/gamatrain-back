namespace GamaEdtech.Data.Dto.ExamImport
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// A past paper on gamatrain (gama's <c>tests</c>): one that matches an exam's details, one of the latest papers,
    /// or the paper an exam is made from (<c>load_paper</c>, with its exam details and a download link to each file).
    /// On gamatrain a board is gama's <c>section</c>, a grade its <c>base</c>, a subject its <c>lesson</c> and the
    /// classification its <c>test_type</c> (Paper 1..6).
    /// </summary>
    public sealed class ExamImportPastPaperDto
    {
        public long Id { get; set; }

        public string? Title { get; set; }

        public int? BoardId { get; set; }

        public string? Board { get; set; }

        public int? GradeId { get; set; }

        public string? Grade { get; set; }

        public int? CourseId { get; set; }

        public int? SubjectId { get; set; }

        public string? Subject { get; set; }

        /// <summary>E.g. Paper 2: the exam's paper type when one has the same title.</summary>
        public string? Classification { get; set; }

        /// <summary>The exam paper type (<c>list_options</c> kind=paper) with the classification's title, for <c>set_exam_details</c>.</summary>
        public int? PaperId { get; set; }

        public int? Year { get; set; }

        public int? Month { get; set; }

        /// <summary>True when an online exam is already linked to it; null when unknown (gama-api's paper lists don't say).</summary>
        public bool? ExamLinked { get; set; }

        /// <summary>True when the caller owns or manages the paper, so gama-api gives them its files for free (not shown to the AI).</summary>
        [JsonIgnore]
        public bool Managed { get; set; }

        /// <summary>The files it has; with a download link each once loaded.</summary>
        public IReadOnlyList<FileDto>? Files { get; set; }

        public sealed class FileDto
        {
            /// <summary>gama-api's file type: pdf (the question paper), word (the question paper as Word), answer (the mark scheme) or extra.</summary>
            public required string Type { get; set; }

            /// <summary>An extra file's id.</summary>
            public long? ExtraId { get; set; }

            /// <summary>What an extra file is, e.g. Insert.</summary>
            public string? Label { get; set; }

            public string? Extension { get; set; }

            /// <summary>No price, or the caller already paid for it (not shown to the AI).</summary>
            [JsonIgnore]
            public bool Free { get; set; }

            /// <summary>gama-api's temporary download link (about an hour).</summary>
            public Uri? Url { get; set; }

            /// <summary>Why there is no link.</summary>
            public string? Error { get; set; }
        }
    }
}
