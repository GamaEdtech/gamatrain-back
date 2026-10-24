namespace GamaEdtech.Presentation.ViewModel.Exam
{
    using System.Collections.Generic;
    using System.Text.Json.Nodes;

    /// <summary>The public exam page's data (<c>GET exams/{id}</c>).</summary>
    public sealed class ExamPageResponseViewModel
    {
        /// <summary>gama-api's <c>exams/{id}</c> data, unchanged: its keys stay snake_case.</summary>
        public JsonObject? Exam { get; set; }

        /// <summary>The first few questions in exam order, without answers. Empty when none could be loaded.</summary>
        public IEnumerable<ExamSampleQuestionViewModel>? SampleQuestions { get; set; }

        /// <summary>The past paper the exam was made from, or null.</summary>
        public ExamPaperLinkViewModel? Paper { get; set; }
    }

    public sealed class ExamSampleQuestionViewModel
    {
        public long Id { get; set; }

        /// <summary>The question's own page is <c>/test/{code}</c>.</summary>
        public string? Code { get; set; }

        /// <summary>Sanitized HTML; may hold TeX (<c>$...$</c>).</summary>
        public string? Question { get; set; }

        public string? Image { get; set; }

        public IEnumerable<ExamSampleOptionViewModel>? Options { get; set; }
    }

    public sealed class ExamSampleOptionViewModel
    {
        /// <summary>1-4, the number gama-api's correct answer uses for this option.</summary>
        public int Key { get; set; }

        /// <summary>Sanitized HTML.</summary>
        public string? Text { get; set; }

        public string? Image { get; set; }
    }

    public sealed class ExamPaperLinkViewModel
    {
        public long Id { get; set; }

        public string? Title { get; set; }

        /// <summary>The paper page's slug: <c>/paper/{id}/{slug}</c>.</summary>
        public string? Slug { get; set; }

        /// <summary>A real exam paper, not a topical question set: only then is the exam a past-paper test.</summary>
        public bool IsPastPaper { get; set; }
    }
}
