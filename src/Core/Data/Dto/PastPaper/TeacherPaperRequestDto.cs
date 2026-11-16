namespace GamaEdtech.Data.Dto.PastPaper
{
    /// <summary>A teacher's request about their own past papers. gama-api answers it as the signed-in user its token belongs
    /// to; this backend answers it for <see cref="UserId"/>.</summary>
    public sealed class TeacherPaperRequestDto
    {
        /// <summary>The caller (this backend's user id).</summary>
        public long UserId { get; set; }

        /// <summary>The caller's token, forwarded to gama-api while the route is <c>Legacy</c>.</summary>
        public required string Token { get; set; }

        /// <summary>Through the admin panel (its permission already checked): every paper, as gama-api's admins see and
        /// manage them, instead of the caller's own.</summary>
        public bool Manager { get; set; }

        /// <summary>The paper, for a request about one paper.</summary>
        public long Id { get; set; }

        /// <summary>The list's query string (gama-api's <c>GET tests</c> parameters), for the list.</summary>
        public string? Query { get; set; }

        /// <summary>The paper's fields as gama-api's <c>POST tests</c> and <c>PUT tests/{id}</c> take them, for an add or an edit.</summary>
        public System.Text.Json.Nodes.JsonObject? Body { get; set; }

        /// <summary>What a delete removes: <c>all</c> (the paper), <c>pdf</c>, <c>word</c>, <c>answer</c>, or <c>extra</c> (then
        /// <see cref="Id"/> is the extra file's id).</summary>
        public string? FileType { get; set; }
    }
}
