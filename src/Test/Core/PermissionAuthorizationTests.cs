namespace GamaEdtech.Test.Core
{
    using System.Collections.Generic;
    using System.Security.Claims;

    using GamaEdtech.Common.Identity;
    using GamaEdtech.Common.Mvc.Routing;

    using Xunit;

    // A non-admin (e.g. a teacher who manages posts) passes an admin endpoint only through a Permission claim naming
    // that exact endpoint; the claim never opens a sibling endpoint or another admin area.
    public class PermissionAuthorizationTests
    {
        private const string UpdatePost = "GamaEdtech.Presentation.Api.Areas.Admin.Controllers.PostsController.UpdatePost (GamaEdtech.Presentation.Api)";
        private const string RemovePost = "GamaEdtech.Presentation.Api.Areas.Admin.Controllers.PostsController.RemovePost (GamaEdtech.Presentation.Api)";
        private const string GetPayments = "GamaEdtech.Presentation.Api.Areas.Admin.Controllers.PaymentsController.GetPayments (GamaEdtech.Presentation.Api)";

        private static readonly PermissionAttribute AdminOnly = new() { Roles = ["Admin"] };

        private static ClaimsPrincipal User(string role, params string[] permissions)
        {
            List<Claim> claims = [new(ClaimTypes.Role, role)];
            claims.AddRange(permissions.Select(t => new Claim(PermissionConstants.PermissionPolicy, t)));
            return new(new ClaimsIdentity(claims, "test"));
        }

        [Fact]
        public void AdminPassesByRole() =>
            Assert.True(PermissionAuthorization.IsAuthorized(User("Admin"), AdminOnly, GetPayments));

        [Fact]
        public void TeacherWithGrantPassesThatEndpoint() =>
            Assert.True(PermissionAuthorization.IsAuthorized(User("Teacher", UpdatePost), AdminOnly, UpdatePost));

        [Fact]
        public void GrantMatchIgnoresCase() =>
            Assert.True(PermissionAuthorization.IsAuthorized(User("Teacher", UpdatePost.ToUpperInvariant()), AdminOnly, UpdatePost));

        [Fact]
        public void GrantDoesNotOpenSiblingEndpoint() =>
            Assert.False(PermissionAuthorization.IsAuthorized(User("Teacher", UpdatePost), AdminOnly, RemovePost));

        [Fact]
        public void GrantDoesNotOpenOtherAdminArea() =>
            Assert.False(PermissionAuthorization.IsAuthorized(User("Teacher", UpdatePost), AdminOnly, GetPayments));

        [Fact]
        public void TeacherWithoutGrantIsRefused() =>
            Assert.False(PermissionAuthorization.IsAuthorized(User("Teacher"), AdminOnly, UpdatePost));

        [Fact]
        public void EndpointWithoutPermissionAttributeIsRefused() =>
            Assert.False(PermissionAuthorization.IsAuthorized(User("Admin"), null, UpdatePost));

        [Fact]
        public void PermissionKeyIsAreaControllerAction() =>
            Assert.Equal("Admin.Posts.GetPosts", PermissionAuthorization.PermissionKey(new Endpoint
            {
                Area = new("Admin", "Admin"),
                Controller = new("Posts", "Posts"),
                Action = new("GetPosts", "Posts List"),
            }));

        [Fact]
        public void PermissionKeyIsNullOutsideAnArea() =>
            Assert.Null(PermissionAuthorization.PermissionKey(new Endpoint
            {
                Controller = new("Posts", "Posts"),
                Action = new("GetPosts", "GetPosts"),
            }));
    }
}
