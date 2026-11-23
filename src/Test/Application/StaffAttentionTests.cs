namespace GamaEdtech.Test.Application
{
    using GamaEdtech.Presentation.Api.Areas.Admin.Controllers;

    using Xunit;

    // admin/dashboard/summary's queues for each caller: all for an admin, only the granted lists' queues for delegated staff.
    public class StaffAttentionTests
    {
        private static readonly HashSet<string> NoKeys = new(StringComparer.Ordinal);

        [Fact]
        public void AnAdminSeesEveryQueue()
        {
            Assert.True(DashboardController.CanSeeQueue("ContactUs", isAdmin: true, NoKeys));
            Assert.True(DashboardController.CanSeeQueue("Payouts", isAdmin: true, NoKeys));
            Assert.True(DashboardController.CanSeeQueue("PastPapers", isAdmin: true, NoKeys));
        }

        [Fact]
        public void AGrantedListShowsOnlyItsQueue()
        {
            HashSet<string> keys = new(StringComparer.Ordinal) { "Admin.PastPapers.GetPapers", "Admin.PastPapers.Review" };
            Assert.True(DashboardController.CanSeeQueue("PastPapers", isAdmin: false, keys));
            Assert.False(DashboardController.CanSeeQueue("Exams", isAdmin: false, keys));
            Assert.False(DashboardController.CanSeeQueue("Payouts", isAdmin: false, keys));
            Assert.False(DashboardController.CanSeeQueue("ContactUs", isAdmin: false, keys));
        }

        [Fact]
        public void NoGrantSeesNothing()
        {
            HashSet<string> keys = new(StringComparer.Ordinal) { "Admin.Locations.GetCities" };
            Assert.False(DashboardController.CanSeeQueue("PastPapers", isAdmin: false, keys));
            Assert.False(DashboardController.CanSeeQueue("SchoolImages", isAdmin: false, keys));
        }
    }
}
