namespace GamaEdtech.Domain.Enumeration
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    public sealed class TransactionType : Enumeration<TransactionType, short>
    {
        [Display]
        public static readonly TransactionType Unknown = new(nameof(Unknown), 0);

        [Display]
        public static readonly TransactionType EasterEgg = new(nameof(EasterEgg), 1);

        [Display]
        public static readonly TransactionType CorrectTestTimeSubmission = new(nameof(CorrectTestTimeSubmission), 2);

        [Display]
        public static readonly TransactionType CorrectExamSubmission = new(nameof(CorrectExamSubmission), 3);

        [Display]
        public static readonly TransactionType Payment = new(nameof(Payment), 4);

        [Display]
        public static readonly TransactionType AdminIncreaseBalance = new(nameof(AdminIncreaseBalance), 5);

        [Display]
        public static readonly TransactionType DeleteContribution = new(nameof(DeleteContribution), 6);

        [Display]
        public static readonly TransactionType DownloadTest = new(nameof(DownloadTest), 7);

        [Display]
        public static readonly TransactionType DownloadPastPaper = new(nameof(DownloadPastPaper), 8);

        [Display]
        public static readonly TransactionType IncorrectTestTimeSubmission = new(nameof(IncorrectTestTimeSubmission), 9);

        [Display]
        public static readonly TransactionType IncorrectExamSubmission = new(nameof(IncorrectExamSubmission), 10);

        [Display]
        public static readonly TransactionType AdminDecreaseBalance = new(nameof(AdminDecreaseBalance), 11);

        [Display]
        public static readonly TransactionType SuccessfulContribution = new(nameof(SuccessfulContribution), 12);

        public TransactionType()
        {
        }

        public TransactionType(string name, byte value) : base(name, value)
        {
        }
    }
}
