namespace GamaEdtech.Data.Dto.Exam
{
    using System.Collections.Generic;
    using System.Text.Json.Nodes;

    /// <summary>What the public exam page shows (<c>GET exams/{id}</c>): gama-api's exam details as it sends them, plus a
    /// few of the exam's questions and the past paper it was made from, so the page has real content for search engines
    /// in one call.</summary>
    public sealed class ExamPageDto
    {
        /// <summary>gama-api's <c>exams/{id}</c> <c>data</c>, unchanged (snake_case keys), so a field gama-api adds reaches
        /// the page without a change here.</summary>
        public JsonObject? Exam { get; set; }

        public IReadOnlyList<ExamSampleQuestionDto> SampleQuestions { get; set; } = [];

        public ExamPaperLinkDto? Paper { get; set; }
    }

    /// <summary>The cached part of <see cref="ExamPageDto"/>: the same for every visitor, unlike the exam details.</summary>
    public sealed class ExamPageExtrasDto
    {
        public IReadOnlyList<ExamSampleQuestionDto> SampleQuestions { get; set; } = [];

        public ExamPaperLinkDto? Paper { get; set; }
    }

    /// <summary>One of the exam's questions, for showing on its public page. It never carries the correct option or the
    /// worked answer: the page is a preview, the answers come after taking the test.</summary>
    public sealed class ExamSampleQuestionDto
    {
        public long Id { get; set; }

        /// <summary>The question's short code, for its own page (<c>/test/{code}</c>).</summary>
        public string? Code { get; set; }

        /// <summary>Sanitized HTML; may hold TeX (<c>$...$</c>) for MathJax.</summary>
        public string? Question { get; set; }

        public string? Image { get; set; }

        public IReadOnlyList<ExamSampleOptionDto> Options { get; set; } = [];
    }

    public sealed class ExamSampleOptionDto
    {
        /// <summary>The option's number, 1-4, as gama-api's correct answer names it: empty options are left out, so
        /// a list position isn't always the number.</summary>
        public int Key { get; set; }

        /// <summary>Sanitized HTML.</summary>
        public string? Text { get; set; }

        public string? Image { get; set; }
    }

    /// <summary>The published past paper an exam was made from (gama-api's exam <c>paperID</c>), for linking the two pages.</summary>
    public sealed class ExamPaperLinkDto
    {
        public long Id { get; set; }

        /// <summary>The paper's own title, e.g. "Accounting 0452/11 Oct Nov 2024".</summary>
        public string? Title { get; set; }

        /// <summary>The paper page's slug on gamatrain.com (<c>/paper/{id}/{slug}</c>).</summary>
        public string? Slug { get; set; }

        /// <summary>A real exam paper (gama-api's <c>is_paper</c>), not e.g. a topical question set: only then may the exam page
        /// call itself a past paper.</summary>
        public bool IsPastPaper { get; set; }
    }
}
