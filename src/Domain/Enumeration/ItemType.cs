namespace GamaEdtech.Domain.Enumeration
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    public sealed class ItemType : Enumeration<ItemType, byte>
    {
        [Display]
        public static readonly ItemType School = new(nameof(School), 0, "school");

        [Display]
        public static readonly ItemType Post = new(nameof(Post), 1, "post");

        [Display]
        public static readonly ItemType Profile = new(nameof(Profile), 2, "profile");

        /// <summary>A past paper's page (since 2026-11-23, from this backend's copy; gama-api's own sitemap before).</summary>
        [Display]
        public static readonly ItemType Paper = new(nameof(Paper), 3, "paper");

        [Display]
        public static readonly ItemType Exam = new(nameof(Exam), 4, "exam");

        [Display]
        public static readonly ItemType Tutorial = new(nameof(Tutorial), 5, "tutorial");

        public string Identifier { get; }

        public ItemType()
        {
        }

        public ItemType(string name, byte value, string identifier)
            : base(name, value) => Identifier = identifier;
    }
}
