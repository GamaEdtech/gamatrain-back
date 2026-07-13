namespace GamaEdtech.Domain.Enumeration
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    public sealed class ContentType : Enumeration<ContentType, byte>
    {
        [Display]
        public static readonly ContentType PastPaper = new(nameof(PastPaper), 1);

        [Display]
        public static readonly ContentType Test = new(nameof(Test), 2);

        [Display]
        public static readonly ContentType Multimedia = new(nameof(Multimedia), 3);

        [Display]
        public static readonly ContentType Exam = new(nameof(Exam), 4);

        public ContentType()
        {
        }

        public ContentType(string name, byte value)
            : base(name, value)
        {
        }
    }
}
