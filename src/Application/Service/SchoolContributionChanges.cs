namespace GamaEdtech.Application.Service
{
    using System.Diagnostics.CodeAnalysis;

    using GamaEdtech.Data.Dto.School;

    /// <summary>
    /// Keeps only what a school contribution changes. Each section of the school page sends its own field, and a user who
    /// saves it unchanged, or empty, used to leave a contribution the admin saw as empty or identical to the school. A value
    /// equal to the school's, or empty, is dropped (null: confirming keeps the school's value, so an empty value never
    /// cleared anything anyway). Internal for the tests.
    /// </summary>
    internal static class SchoolContributionChanges
    {
        // Coordinates within about a centimetre are the same place.
        private const double SamePlace = 1e-7;

        /// <summary>Drops from <paramref name="contribution"/> every value that changes nothing on <paramref name="school"/>
        /// (null for a new school) and tells whether anything is left. An image or a comment is always a change.</summary>
        public static bool Keep([NotNull] SchoolContributionDto contribution, SchoolDto? school, bool hasImage)
        {
            contribution.Name = Changed(contribution.Name, school?.Name);
            contribution.LocalName = Changed(contribution.LocalName, school?.LocalName);
            contribution.ZipCode = Changed(contribution.ZipCode, school?.ZipCode);
            contribution.Address = Changed(contribution.Address, school?.Address);
            contribution.LocalAddress = Changed(contribution.LocalAddress, school?.LocalAddress);
            contribution.WebSite = Changed(contribution.WebSite, school?.WebSite);
            contribution.FaxNumber = Changed(contribution.FaxNumber, school?.FaxNumber);
            contribution.PhoneNumber = Changed(contribution.PhoneNumber, school?.PhoneNumber);
            contribution.Email = Changed(contribution.Email, school?.Email);
            contribution.Quarter = Changed(contribution.Quarter, school?.Quarter);
            contribution.Description = Changed(contribution.Description, school?.Description);

            contribution.SchoolType = ChangedKind(contribution.SchoolType, school?.SchoolType);
            contribution.CityId = Changed(contribution.CityId, school?.CityId);
            contribution.CountryId = Changed(contribution.CountryId, school?.CountryId);
            contribution.StateId = Changed(contribution.StateId, school?.StateId);
            contribution.Tuition = Changed(contribution.Tuition, school?.Tuition);

            // A place is a pair: confirming moves the school only when both are given, and only somewhere else.
            if (contribution.Latitude is not double latitude || contribution.Longitude is not double longitude
                || (school?.Coordinates is { } point && Math.Abs(point.Y - latitude) < SamePlace && Math.Abs(point.X - longitude) < SamePlace))
            {
                contribution.Latitude = null;
                contribution.Longitude = null;
            }

            // An empty list removes the school's tags or boards: a change only when it has some.
            contribution.Tags = SameSet(contribution.Tags, school?.Tags?.Select(t => t.Id)) ? null : contribution.Tags;
            contribution.Boards = SameSet(contribution.Boards, school?.Boards?.Select(t => t.Id)) ? null : contribution.Boards;

            List<SchoolLocalizedValueDto> localized = [.. (contribution.LocalizedValues ?? []).Where(t =>
                !string.IsNullOrWhiteSpace(t.Name) || !string.IsNullOrWhiteSpace(t.Address) || !string.IsNullOrWhiteSpace(t.Description))];
            contribution.LocalizedValues = localized.Count > 0 ? localized : null;

            return hasImage || contribution.Comment is not null || contribution.DefaultImageId.HasValue || contribution.LocalizedValues is not null
                || contribution.Name is not null || contribution.LocalName is not null || contribution.ZipCode is not null
                || contribution.Address is not null || contribution.LocalAddress is not null || contribution.WebSite is not null
                || contribution.FaxNumber is not null || contribution.PhoneNumber is not null || contribution.Email is not null
                || contribution.Quarter is not null || contribution.Description is not null || contribution.SchoolType is not null
                || contribution.CityId.HasValue || contribution.CountryId.HasValue || contribution.StateId.HasValue
                || contribution.Tuition.HasValue || contribution.Latitude.HasValue || contribution.Tags is not null || contribution.Boards is not null;
        }

        private static string? Changed(string? value, string? current)
        {
            var text = value?.Trim();
            return string.IsNullOrEmpty(text) || string.Equals(text, current?.Trim(), StringComparison.Ordinal) ? null : text;
        }

        private static T? Changed<T>(T? value, T? current)
            where T : struct => value.HasValue && !EqualityComparer<T?>.Default.Equals(value, current) ? value : null;

        private static T? ChangedKind<T>(T? value, T? current)
            where T : class => value is not null && !Equals(value, current) ? value : null;

        private static bool SameSet<T>(IEnumerable<T>? value, IEnumerable<T>? current) =>
            value is null || value.ToHashSet().SetEquals(current ?? []);
    }
}
