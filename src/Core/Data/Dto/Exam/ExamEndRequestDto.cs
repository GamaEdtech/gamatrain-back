namespace GamaEdtech.Data.Dto.Exam
{
    using System.Text.Json.Nodes;

    /// <summary>A student's exam submit, forwarded to gama-api's <c>exams/end/{id}</c> as the browser sent it.</summary>
    public sealed class ExamEndRequestDto
    {
        /// <summary>The exam's id on gama-api.</summary>
        public long ExamId { get; set; }

        /// <summary>The student's gama-api token.</summary>
        public required string SecretKey { get; set; }

        /// <summary>The body gama-api expects (<c>startID</c>, <c>answers</c>), unchanged.</summary>
        public required JsonObject Body { get; set; }

        /// <summary>The student here; the native source records the attempt under it (a source never reads the request).</summary>
        public long? UserId { get; set; }
    }
}
