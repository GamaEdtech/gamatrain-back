namespace GamaEdtech.Infrastructure.EntityFramework.Context
{
    using GamaEdtech.Domain.Entity.Identity;

    using GamaEdtech.Common.DataAnnotation;

    using global::EntityFramework.Exceptions.SqlServer;

    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.DependencyInjection;

    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Reflection;
    using GamaEdtech.Common.DataAccess.Context;
    using GamaEdtech.Domain;
    using GamaEdtech.Infrastructure.Common.Utilities;

    [ServiceLifetime(ServiceLifetime.Transient, "System.IServiceProvider,System.ComponentModel")]
    public class ApplicationDBContext(IServiceProvider serviceProvider) : IdentityEntityContext<ApplicationDBContext, ApplicationUser, ApplicationRole,
        int, ApplicationUserClaim, ApplicationUserRole, ApplicationUserLogin, ApplicationRoleClaim, ApplicationUserToken>(serviceProvider)
    {
        protected override Assembly EntityAssembly => typeof(ApplicationUser).Assembly;

        protected override void OnConfiguring([NotNull] DbContextOptionsBuilder optionsBuilder)
        {
            base.OnConfiguring(optionsBuilder);
            _ = optionsBuilder.UseSqlServer(ConnectionName, t =>
            {
                _ = t.CommandTimeout(60 * 5);
                _ = t.UseNetTopologySuite();
                _ = t.UseHierarchyId();
            });
            _ = optionsBuilder.UseExceptionProcessor();
        }

        protected override void OnModelCreating([NotNull] ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            var entityAssembly = typeof(IEntity).Assembly;

            _ = builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly(),
                config => config.IsClass && !config.IsAbstract && config.IsPublic);

            builder.RegisterAllEntities<IEntity>(entityAssembly);
            builder.RegisterEntityTypeConfiguration(entityAssembly);
            builder.AddRestrictDeleteBehaviorConvention<IEntity>();
            builder.AddPluralizingTableNameConvention<IEntity>();
        }
    }
}
