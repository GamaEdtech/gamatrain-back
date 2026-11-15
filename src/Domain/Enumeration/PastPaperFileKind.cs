namespace GamaEdtech.Domain.Enumeration
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>What a past paper file is. A paper has at most one of each kind except <see cref="Extra"/>.</summary>
    public sealed class PastPaperFileKind : Enumeration<PastPaperFileKind, byte>
    {
        /// <summary>The question paper (gama-api's <c>q_file</c>).</summary>
        [Display]
        public static readonly PastPaperFileKind Question = new(nameof(Question), 1);

        /// <summary>The answer or mark scheme (gama-api's <c>a_file</c>).</summary>
        [Display]
        public static readonly PastPaperFileKind Answer = new(nameof(Answer), 2);

        /// <summary>The question paper as a Word file (gama-api's <c>q_file_word</c>).</summary>
        [Display]
        public static readonly PastPaperFileKind QuestionWord = new(nameof(QuestionWord), 3);

        /// <summary>Any other file (gama-api's <c>tests_extra_files</c>: inserts, audio, examiner reports...); what it
        /// is comes from <c>PastPaperFile.CoreTypeId</c>.</summary>
        [Display]
        public static readonly PastPaperFileKind Extra = new(nameof(Extra), 4);

        public PastPaperFileKind()
        {
        }

        public PastPaperFileKind(string name, byte value) : base(name, value)
        {
        }
    }
}
