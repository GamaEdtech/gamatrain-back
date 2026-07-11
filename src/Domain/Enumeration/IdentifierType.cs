namespace GamaEdtech.Domain.Enumeration
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>
    /// Which kind of id a "target user" API parameter refers to - the local ApplicationUser.Id (default) or the
    /// legacy gama-api ApplicationUser.CoreId.
    /// </summary>
    public sealed class IdentifierType : Enumeration<IdentifierType, byte>
    {
        [Display]
        public static readonly IdentifierType Id = new(nameof(Id), 0);

        [Display]
        public static readonly IdentifierType CoreId = new(nameof(CoreId), 1);

        public IdentifierType()
        {
        }

        public IdentifierType(string name, byte value) : base(name, value)
        {
        }
    }
}
