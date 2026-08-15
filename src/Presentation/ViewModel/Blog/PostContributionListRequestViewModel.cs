namespace GamaEdtech.Presentation.ViewModel.Blog
{
    using System.Text.Json.Serialization;

    using GamaEdtech.Common.Converter;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAnnotation;
    using GamaEdtech.Domain.Enumeration;

    public sealed class PostContributionListRequestViewModel
    {
        [Display]
        public PagingDto? PagingDto { get; set; } = new() { PageFilter = new(), };

        /// <summary>
        /// Optional - omitting it returns every status except <see cref="Status.Deleted"/> (see
        /// <c>BlogsController.GetPostContributionList</c>). Declared nullable deliberately: a non-nullable
        /// reference-typed property here is implicitly required by ASP.NET Core's model validation (Nullable
        /// Reference Types are enabled solution-wide, and `SuppressImplicitRequiredAttributeForNonNullable
        /// ReferenceTypes` is never set) - the previous non-nullable `Status Status` silently 400'd every
        /// request that omitted it, even though the controller's own logic was written to treat that as
        /// "no filter". Fixed 2026-08-15.
        /// </summary>
        [Display]
        [JsonConverter(typeof(EnumerationConverter<Status, byte>))]
        public Status? Status { get; set; }

        [Display]
        public DateTimeOffset? StartDate { get; set; }

        [Display]
        public DateTimeOffset? EndDate { get; set; }

        [Display]
        public string? Email { get; set; }

        [Display]
        public string? Username { get; set; }
    }
}
