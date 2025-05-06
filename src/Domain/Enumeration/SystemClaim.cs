namespace GamaEdtech.Domain.Enumeration
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    using System.Collections;

    public sealed class SystemClaim : FlagsEnumeration<SystemClaim>
    {
        [Display]
        public static readonly SystemClaim AutoConfirmSchoolContribution = new(1);

        [Display]
        public static readonly SystemClaim AutoConfirmSchoolImage = new(2);

        [Display]
        public static readonly SystemClaim AutoConfirmSchoolComment = new(3);


        [Display]
        public static readonly SystemClaim AutoConfirmPost = new(4);

        public SystemClaim()
        {
        }

        public SystemClaim(BitArray value)
            : base(value)
        {
        }

        public SystemClaim(byte[] value)
            : base(value)
        {
        }

        public SystemClaim(int index)
            : base(index)
        {
        }
    }
}
