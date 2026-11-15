namespace GamaEdtech.Domain.Enumeration
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    public sealed class ContainerType : Enumeration<ContainerType, byte>
    {
        [Display]
        public static readonly ContainerType Default = new(nameof(Default), 0);

        [Display]
        public static readonly ContainerType School = new(nameof(School), 1);

        [Display]
        public static readonly ContainerType Post = new(nameof(Post), 2);

        [Display]
        public static readonly ContainerType Ticket = new(nameof(Ticket), 3);

        [Display]
        public static readonly ContainerType User = new(nameof(User), 4);

        /// <summary>Past paper files (question, answer, Word, extra files).</summary>
        [Display]
        public static readonly ContainerType PastPaper = new(nameof(PastPaper), 5, isPrivate: true);

        // Built once from the declared members, so a copy made by model binding or JSON (which only sets the value)
        // answers the same as the member it stands for.
        private static readonly Lazy<HashSet<byte>> PrivateValues = new(() =>
            [.. EnumerationExtensions.GetAll<ContainerType, byte>()!.Where(t => t is { isPrivate: true }).Select(t => t!.Value)]);

        private readonly bool isPrivate;

        public ContainerType()
        {
        }

        public ContainerType(string name, byte value) : base(name, value)
        {
        }

        private ContainerType(string name, byte value, bool isPrivate) : base(name, value) => this.isPrivate = isPrivate;

        /// <summary>
        /// Whether the container holds private files (anything sold or owned, not only papers). Its files are stored in
        /// the private bucket, never get a permanent (CDN) URL, and are only reached through a short signed URL handed out
        /// after the access check. A new private container is declared with <c>isPrivate: true</c>.
        /// </summary>
        public bool IsPrivate() => PrivateValues.Value.Contains(Value);
    }
}
