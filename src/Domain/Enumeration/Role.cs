namespace GamaEdtech.Domain.Enumeration
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    using System.Collections;

    public sealed class Role : FlagsEnumeration<Role>
    {
        [Display]
        public static readonly Role Admin = new(1);

        [Display]
        public static readonly Role Teacher = new(2);

        [Display]
        public static readonly Role Student = new(3);

        [Display]
        public static readonly Role Advisor = new(4);

        [Display]
        public static readonly Role Finance = new(5);

        /// <summary>A parent of a student. Given by joining a school as a parent (SchoolMemberRole.Parent).</summary>
        [Display]
        public static readonly Role Parent = new(6);

        public Role()
        {
        }

        public Role(BitArray value)
            : base(value)
        {
        }

        public Role(byte[] value)
            : base(value)
        {
        }

        public Role(int index)
            : base(index)
        {
        }
    }
}
