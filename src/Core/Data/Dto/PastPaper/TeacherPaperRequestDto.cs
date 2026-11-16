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

        /// <summary>The paper, for a request about one paper.</summary>
        public long Id { get; set; }

        /// <summary>The list's query string (gama-api's <c>GET tests</c> parameters), for the list.</summary>
        public string? Query { get; set; }
    }
}
