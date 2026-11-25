namespace GamaEdtech.Test.Application
{
    using GamaEdtech.Application.Service;
    using GamaEdtech.Common.DataAccess.Context;
    using GamaEdtech.Common.DataAccess.Repositories;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Domain.Entity;
    using GamaEdtech.Domain.Entity.Identity;
    using GamaEdtech.Domain.Enumeration;

    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging.Abstractions;

    using Xunit;

    using TestApplicationDBContext = GamaEdtech.Test.Application.ExamAnalysisServiceTests.TestApplicationDBContext;

    // The admin report of school memberships: filters, newest first, and the count per role of every matching one.
    public sealed class SchoolMemberAdminTests : IDisposable
    {
        private readonly ServiceProvider serviceProvider;
        private readonly TestApplicationDBContext context;

        public SchoolMemberAdminTests()
        {
            Environment.SetEnvironmentVariable("Test", "True");
            ServiceCollection services = new();
            _ = services.AddLogging();
            _ = services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["EnableAudit"] = bool.FalseString }).Build());
            _ = services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            _ = services.AddSingleton<TestApplicationDBContext>();
            _ = services.AddSingleton<IEntityContext>(provider => provider.GetRequiredService<TestApplicationDBContext>());
            _ = services.AddTransient(typeof(IRepository<,>), typeof(GenericEntityRepository<,>));
            _ = services.AddSingleton<IUnitOfWorkProvider, UnitOfWorkProvider>();
            serviceProvider = services.BuildServiceProvider();
            context = serviceProvider.GetRequiredService<TestApplicationDBContext>();

            var now = DateTimeOffset.UtcNow;
            context.Set<School>().AddRange(new School { Id = 1, Name = "Hill School" }, new School { Id = 2, Name = "River School" });
            context.Set<ApplicationUser>().AddRange(
                new ApplicationUser { Id = 10, UserName = "ana", NormalizedUserName = "ANA", ProfileVisibility = ProfileVisibility.Private, Email = "ana@example.com", FirstName = "Ana", LastName = "Lee", Enabled = true },
                new ApplicationUser { Id = 11, UserName = "ben", NormalizedUserName = "BEN", ProfileVisibility = ProfileVisibility.Private, Email = "ben@example.com", FirstName = "Ben", LastName = "Ray", Enabled = true },
                new ApplicationUser { Id = 12, UserName = "cy", NormalizedUserName = "CY", ProfileVisibility = ProfileVisibility.Private, Email = "cy@example.com", FirstName = "Cy", LastName = "Dow", Enabled = true });
            context.Set<SchoolMember>().AddRange(
                new SchoolMember { Id = 1, UserId = 10, SchoolId = 1, Role = SchoolMemberRole.Teacher, ShowName = true, CreationDate = now.AddDays(-3) },
                new SchoolMember { Id = 2, UserId = 10, SchoolId = 1, Role = SchoolMemberRole.Parent, CreationDate = now.AddDays(-2) },
                new SchoolMember { Id = 3, UserId = 11, SchoolId = 1, Role = SchoolMemberRole.Student, CreationDate = now.AddDays(-1) },
                new SchoolMember { Id = 4, UserId = 12, SchoolId = 2, Role = SchoolMemberRole.Student, CreationDate = now });
            _ = context.SaveChanges();
        }

        [Fact]
        public async Task TheReportFiltersAndCountsEveryRole()
        {
            var service = new SchoolMemberService(new(serviceProvider.GetRequiredService<IUnitOfWorkProvider>), new(serviceProvider.GetRequiredService<IHttpContextAccessor>),
                new(() => null!), new(() => NullLogger<SchoolMemberService>.Instance), new(() => null!), new(() => null!), new(() => null!));

            var all = (await service.GetMembershipsAsync(new())).Data!;
            Assert.Equal((4, 3, 2), (all.Total, all.Users, all.Schools));
            Assert.Equal([4L, 3L, 2L, 1L], all.Items.Select(t => t.Id));
            Assert.Equal(("Cy", "River School"), (all.Items[0].FirstName, all.Items[0].SchoolName));

            // The role filter narrows the list but not the counts, so each role's tab shows its own number.
            var students = (await service.GetMembershipsAsync(new() { Role = SchoolMemberRole.Student })).Data!;
            Assert.Equal(2, students.Total);
            Assert.Equal(4, students.RoleCounts.Sum(t => t.Count));
            Assert.Equal(2, students.RoleCounts.Single(t => t.Role == SchoolMemberRole.Student).Count);

            Assert.Equal(2, (await service.GetMembershipsAsync(new() { Text = "ana@" })).Data!.Total);
            Assert.Equal(3, (await service.GetMembershipsAsync(new() { Text = "Hill" })).Data!.Total);
            Assert.Equal(1, (await service.GetMembershipsAsync(new() { SchoolId = 2 })).Data!.Total);
        }

        public void Dispose()
        {
            context.Dispose();
            serviceProvider.Dispose();
        }
    }
}
