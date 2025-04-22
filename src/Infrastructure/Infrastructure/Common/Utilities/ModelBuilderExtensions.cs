namespace GamaEdtech.Infrastructure.Common.Utilities
{
    using System.Diagnostics.CodeAnalysis;
    using System.Reflection;

    using GamaEdtech.Domain;

    using Microsoft.EntityFrameworkCore;

    using Pluralize.NET;

    public static class ModelBuilderExtensions
    {
        public static void AddSingularizingTableNameConvention([NotNull] this ModelBuilder modelBuilder)
        {
            var pluralizer = new Pluralizer();
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                var tableName = entityType.GetTableName();
                entityType.SetTableName(pluralizer.Singularize(tableName));
            }
        }

        public static void AddPluralizingTableNameConvention<TBaseEntity>([NotNull] this ModelBuilder modelBuilder)
            where TBaseEntity : class, IEntity
        {
            var pluralizer = new Pluralizer();
            foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(c => c is TBaseEntity))
            {
                var tableName = entityType.GetTableName();
                entityType.SetTableName(pluralizer.Pluralize(tableName));
            }
        }

        public static void AddSequentialGuidForIdConvention(this ModelBuilder modelBuilder) =>
            modelBuilder.AddDefaultValueSqlConvention("Id", typeof(Guid), "NEWSEQUENTIALID()");

        public static void AddDefaultValueSqlConvention([NotNull] this ModelBuilder modelBuilder, string propertyName, Type propertyType, string defaultValueSql)
        {
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                var property = entityType.GetProperties().SingleOrDefault(p => p.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
                if (property != null && property.ClrType == propertyType)
                {
                    property.SetDefaultValueSql(defaultValueSql);
                }
            }
        }

        public static void AddRestrictDeleteBehaviorConvention<TBaseEntity>([NotNull] this ModelBuilder modelBuilder)
            where TBaseEntity : class, IEntity
        {
            var cascadeFKs = modelBuilder.Model.GetEntityTypes()
                .Where(c => c is TBaseEntity)
                .SelectMany(t => t.GetForeignKeys())
                .Where(fk => !fk.IsOwnership && fk.DeleteBehavior == DeleteBehavior.Cascade);
            foreach (var fk in cascadeFKs)
            {
                fk.DeleteBehavior = DeleteBehavior.Restrict;
            }
        }

        public static void RegisterEntityTypeConfiguration(this ModelBuilder modelBuilder, params Assembly[] assemblies)
        {
            var applyGenericMethod = typeof(ModelBuilder).GetMethods().First(m => m.Name == nameof(ModelBuilder.ApplyConfiguration));

            var types = assemblies.SelectMany(a => a.GetExportedTypes())
                .Where(c => c.IsClass && !c.IsAbstract && c.IsPublic);

            foreach (var type in types)
            {
                foreach (var iface in type.GetInterfaces())
                {
                    if (iface.IsConstructedGenericType && iface.GetGenericTypeDefinition() == typeof(IEntityTypeConfiguration<>))
                    {
                        var applyConcreteMethod = applyGenericMethod.MakeGenericMethod(iface.GenericTypeArguments[0]);
                        _ = applyConcreteMethod.Invoke(modelBuilder, [Activator.CreateInstance(type)]);
                    }
                }
            }
        }

        public static void RegisterAllEntities<TBaseType>([NotNull] this ModelBuilder modelBuilder, params Assembly[] assemblies)
        {
            var types = assemblies.SelectMany(a => a.GetExportedTypes())
                .Where(c => c.IsClass && !c.IsAbstract && c.IsPublic && typeof(TBaseType).IsAssignableFrom(c));

            foreach (var type in types)
            {
                _ = modelBuilder.Entity(type);
            }
        }
    }
}
