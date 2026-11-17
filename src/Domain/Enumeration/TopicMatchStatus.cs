namespace GamaEdtech.Domain.Enumeration
{
    using GamaEdtech.Common.Data.Enumeration;
    using GamaEdtech.Common.DataAnnotation;

    /// <summary>A suggested topic match's review status, gama-api's own <c>topic_found_matches.status</c> codes.</summary>
    public sealed class TopicMatchStatus : Enumeration<TopicMatchStatus, byte>
    {
        /// <summary>Waiting for a review.</summary>
        [Display]
        public static readonly TopicMatchStatus Pending = new(nameof(Pending), 0);

        /// <summary>Approved: the two topics are linked (<c>LegacyTaxonomyRelations</c>).</summary>
        [Display]
        public static readonly TopicMatchStatus Approved = new(nameof(Approved), 1);

        /// <summary>gama-api's code for a rejected match; gama-api deletes a rejected suggestion instead, and so does this backend.</summary>
        [Display]
        public static readonly TopicMatchStatus Rejected = new(nameof(Rejected), 2);

        public TopicMatchStatus()
        {
        }

        public TopicMatchStatus(string name, byte value) : base(name, value)
        {
        }
    }
}
