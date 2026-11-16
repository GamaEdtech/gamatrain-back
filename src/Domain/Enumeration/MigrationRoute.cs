namespace GamaEdtech.Domain.Enumeration
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>
    /// Where a <see cref="MigrationDomain"/> is served from. It is also the key of that domain's sources: the gama-api
    /// proxy is the <see cref="Legacy"/> one, this backend's own code and data the <see cref="Native"/> one.
    /// </summary>
    public sealed class MigrationRoute : Enumeration<MigrationRoute, byte>
    {
        /// <summary>gama-api answers. The default, and what an unknown or missing setting means.</summary>
        [Display]
        public static readonly MigrationRoute Legacy = new(nameof(Legacy), 0);

        /// <summary>gama-api answers; the native source gets a copy of the call afterwards and only the difference is recorded.</summary>
        [Display]
        public static readonly MigrationRoute Shadow = new(nameof(Shadow), 1);

        /// <summary>This backend answers from its own data.</summary>
        [Display]
        public static readonly MigrationRoute Native = new(nameof(Native), 2);

        public MigrationRoute()
        {
        }

        public MigrationRoute(string name, byte value) : base(name, value)
        {
        }
    }
}
