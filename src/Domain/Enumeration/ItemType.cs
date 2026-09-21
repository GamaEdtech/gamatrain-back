namespace GamaEdtech.Domain.Enumeration
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    public sealed class ItemType : Enumeration<ItemType, byte>
    {
        [Display]
        public static readonly ItemType School = new(nameof(School), 0, "school");

        [Display]
        public static readonly ItemType Post = new(nameof(Post), 1, "blog");

        [Display]
        public static readonly ItemType Profile = new(nameof(Profile), 2, "profile");

        public string Identifier { get; }

        public ItemType()
        {
        }

        public ItemType(string name, byte value, string identifier)
            : base(name, value) => Identifier = identifier;
    }
}
