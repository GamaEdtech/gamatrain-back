namespace GamaEdtech.Test.Application
{
    using GamaEdtech.Presentation.Api.Controllers;

    using Xunit;

    // The "needs attention" counts a delegated user sees: only the queues of the admin lists they were granted.
    public class StaffAttentionTests
    {
        [Fact]
        public void AnAdminSeesEveryQueue()
        {
            HashSet<string> keys = new(StringComparer.Ordinal) { "Admin.Dashboard.GetSummary" };
            Assert.True(IdentitiesController.CanSeeAttention(keys));
            Assert.True(IdentitiesController.CanSeeQueue("ContactUs", keys));
            Assert.True(IdentitiesController.CanSeeQueue("PastPapers", keys));
        }

        [Fact]
        public void AGrantedListShowsOnlyItsQueue()
        {
            HashSet<string> keys = new(StringComparer.Ordinal) { "Admin.PastPapers.GetPapers", "Admin.PastPapers.Review" };
            Assert.True(IdentitiesController.CanSeeAttention(keys));
            Assert.True(IdentitiesController.CanSeeQueue("PastPapers", keys));
            Assert.False(IdentitiesController.CanSeeQueue("Exams", keys));
            Assert.False(IdentitiesController.CanSeeQueue("Payouts", keys));
            Assert.False(IdentitiesController.CanSeeQueue("ContactUs", keys));
        }

        [Fact]
        public void NoGrantSeesNothing()
        {
            HashSet<string> keys = new(StringComparer.Ordinal) { "Admin.Locations.GetCities" };
            Assert.False(IdentitiesController.CanSeeAttention(keys));
            Assert.False(IdentitiesController.CanSeeQueue("PastPapers", keys));
        }
    }
}
