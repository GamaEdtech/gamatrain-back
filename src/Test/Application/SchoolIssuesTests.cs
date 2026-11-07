namespace GamaEdtech.Test.Application
{
    using System.Collections.ObjectModel;
    using System.Reflection;

    using GamaEdtech.Application.Interface;
    using GamaEdtech.Application.Service;
    using GamaEdtech.Common.Data;
    using GamaEdtech.Common.DataAccess.Context;
    using GamaEdtech.Common.DataAccess.Repositories;
    using GamaEdtech.Common.DataAccess.Specification.Impl;
    using GamaEdtech.Common.DataAccess.UnitOfWork;
    using GamaEdtech.Data.Dto.Contribution;
    using GamaEdtech.Data.Dto.Email;
    using GamaEdtech.Domain.Entity;

    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Localization;
    using Microsoft.Extensions.Logging.Abstractions;

    using Xunit;

    using static GamaEdtech.Common.Core.Constants;

    /// <summary>
    /// Confirming a school issue soft-deletes the school, so the issue lists must still find its name, and the
    /// confirmation email must carry the reported issue, not the school name a second time.
    /// </summary>
    public sealed class SchoolIssuesTests : IDisposable
    {
        private const long LiveSchoolId = 1;
        private const long DeletedSchoolId = 2;

        private readonly ExamAnalysisServiceTests.TestApplicationDBContext context;
        private readonly ServiceProvider serviceProvider;

        public SchoolIssuesTests()
        {
            Environment.SetEnvironmentVariable("Test", "True");

            ServiceCollection services = new();
            _ = services.AddLogging();
            _ = services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EnableAudit"] = bool.FalseString,
            }).Build());
            _ = services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            _ = services.AddSingleton<ExamAnalysisServiceTests.TestApplicationDBContext>();
            _ = services.AddSingleton<IEntityContext>(provider => provider.GetRequiredService<ExamAnalysisServiceTests.TestApplicationDBContext>());
            _ = services.AddTransient(typeof(IRepository<,>), typeof(GenericEntityRepository<,>));
            _ = services.AddSingleton<IUnitOfWorkProvider, UnitOfWorkProvider>();

            serviceProvider = services.BuildServiceProvider();
            context = serviceProvider.GetRequiredService<ExamAnalysisServiceTests.TestApplicationDBContext>();

            _ = context.Set<School>().Add(new() { Id = LiveSchoolId, Name = "Live School" });
            _ = context.Set<School>().Add(new() { Id = DeletedSchoolId, Name = "Reported School", IsDeleted = true });
            _ = context.SaveChanges();
            context.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            context.Dispose();
            serviceProvider.Dispose();
            GC.SuppressFinalize(this);
        }

        [Fact]
        public async Task NameOfDeletedSchoolIsFoundWhenIncludingDeleted()
        {
            var service = CreateService(new ContributionProxyHolder(), new EmailProxyHolder());

            var result = await service.GetSchoolsNameAsync(new IdContainsSpecification<School, long>([LiveSchoolId, DeletedSchoolId]), includeDeleted: true);

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            Assert.Equal("Reported School", result.Data!.Single(t => t.Key == DeletedSchoolId).Value);
            Assert.Equal("Live School", result.Data!.Single(t => t.Key == LiveSchoolId).Value);
        }

        [Fact]
        public async Task DeletedSchoolStaysHiddenByDefault()
        {
            var service = CreateService(new ContributionProxyHolder(), new EmailProxyHolder());

            var result = await service.GetSchoolsNameAsync(new IdContainsSpecification<School, long>([LiveSchoolId, DeletedSchoolId]));

            Assert.Equal(LiveSchoolId, Assert.Single(result.Data!).Key);
        }

        [Fact]
        public async Task ConfirmationEmailCarriesTheEncodedIssueAndDeletesTheSchool()
        {
            var contributions = new ContributionProxyHolder { Description = "Closed <b>since</b> 2024" };
            var emails = new EmailProxyHolder();
            var service = CreateService(contributions, emails);

            var result = await service.ConfirmSchoolIssuesContributionAsync(new() { ContributionId = 9, NotifyUser = true });

            Assert.Equal(OperationResult.Succeeded, result.OperationResult);
            var email = Assert.Single(emails.Proxy.Sent);
            Assert.Equal("Reporter|Live School|1|Closed &lt;b&gt;since&lt;/b&gt; 2024", email.Body);
            Assert.True((await context.Set<School>().IgnoreQueryFilters().SingleAsync(t => t.Id == LiveSchoolId)).IsDeleted);
        }

        private SchoolService CreateService(ContributionProxyHolder contributions, EmailProxyHolder emails)
        {
            var settings = DispatchProxy.Create<IApplicationSettingsService, SettingsProxy>();

            return new(
                new(serviceProvider.GetRequiredService<IUnitOfWorkProvider>),
                new(() => throw new InvalidOperationException("http context touched")),
                new(() => new KeyLocalizer()),
                new(() => emails.Service),
                new(() => NullLogger<SchoolService>.Instance),
                new(() => throw new InvalidOperationException("file service touched")),
                new(() => contributions.Service),
                new(() => throw new InvalidOperationException("identity touched")),
                new(() => settings),
                new(() => throw new InvalidOperationException("configuration touched")),
                new(() => throw new InvalidOperationException("tags touched")),
                new(() => throw new InvalidOperationException("reactions touched")),
                new(() => throw new InvalidOperationException("locations touched")),
                new(() => throw new InvalidOperationException("boards touched")),
                new(() => throw new InvalidOperationException("localization touched")));
        }

        private sealed class KeyLocalizer : IStringLocalizer<SchoolService>
        {
            public LocalizedString this[string name] => new(name, name);
            public LocalizedString this[string name, params object[] arguments] => new(name, name);
            public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
        }

        private sealed class ContributionProxyHolder
        {
            public ContributionProxyHolder()
            {
                Service = DispatchProxy.Create<IContributionService, ContributionProxy>();
                ((ContributionProxy)Service).Holder = this;
            }

            public IContributionService Service { get; }

            public string? Description { get; set; }
        }

        private sealed class EmailProxyHolder
        {
            public EmailProxyHolder() => Service = DispatchProxy.Create<IEmailService, EmailProxy>();

            public IEmailService Service { get; }

            public EmailProxy Proxy => (EmailProxy)Service;
        }

        public class ContributionProxy : DispatchProxy
        {
            internal object? Holder { get; set; }

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                if (targetMethod?.Name == nameof(IContributionService.ConfirmContributionAsync))
                {
                    var description = ((ContributionProxyHolder)Holder!).Description;
                    return Task.FromResult(new ResultData<ContributionDto<string>>(OperationResult.Succeeded)
                    {
                        Data = new()
                        {
                            Id = 9,
                            Data = description,
                            IdentifierId = LiveSchoolId,
                            Email = "reporter@example.com",
                            FullName = "Reporter",
                        },
                    });
                }

                throw new NotSupportedException(targetMethod?.Name);
            }
        }

        public class SettingsProxy : DispatchProxy
        {
            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
                => targetMethod?.Name == nameof(IApplicationSettingsService.GetSettingAsync)
                    ? Task.FromResult(new ResultData<string?>(OperationResult.Succeeded) { Data = "[RECEIVER_NAME]|[SCHOOL_NAME]|[SCHOOL_ID]|[ISSUES]" })
                    : throw new NotSupportedException(targetMethod?.Name);
        }

        public class EmailProxy : DispatchProxy
        {
            public Collection<SendEmailRequestDto> Sent { get; } = [];

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                if (targetMethod?.Name == nameof(IEmailService.SendEmailAsync))
                {
                    ArgumentNullException.ThrowIfNull(args);
                    Sent.Add((SendEmailRequestDto)args[0]!);
                    return Task.FromResult(new ResultData<GamaEdtech.Common.Data.Void>(OperationResult.Succeeded));
                }

                throw new NotSupportedException(targetMethod?.Name);
            }
        }
    }
}
