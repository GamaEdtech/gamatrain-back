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

        public ContainerType()
        {
        }

        public ContainerType(string name, byte value) : base(name, value)
        {
        }
    }
}
