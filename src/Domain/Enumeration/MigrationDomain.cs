namespace GamaEdtech.Domain.Enumeration
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>
    /// A part of gama-api (the legacy backend) that moves to this backend on its own, in the order of the members. Each one
    /// has its own <see cref="MigrationRoute"/>, so a domain is switched without touching the others. See
    /// <c>docs/architecture/legacy-migration.md</c>.
    /// </summary>
    public sealed class MigrationDomain : Enumeration<MigrationDomain, byte>
    {
        /// <summary>Boards, grades, subjects, topics and the other lookup lists (gama-api's <c>types</c>).</summary>
        [Display]
        public static readonly MigrationDomain Types = new(nameof(Types), 0);

        [Display]
        public static readonly MigrationDomain PastPapers = new(nameof(PastPapers), 1);

        [Display]
        public static readonly MigrationDomain Tutorials = new(nameof(Tutorials), 2);

        [Display]
        public static readonly MigrationDomain Exams = new(nameof(Exams), 3);

        [Display]
        public static readonly MigrationDomain Finance = new(nameof(Finance), 4);

        /// <summary>Users and sign-in. The last one to move.</summary>
        [Display]
        public static readonly MigrationDomain Identity = new(nameof(Identity), 5);

        public MigrationDomain()
        {
        }

        public MigrationDomain(string name, byte value) : base(name, value)
        {
        }
    }
}
