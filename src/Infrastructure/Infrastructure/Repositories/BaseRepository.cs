namespace GamaEdtech.Infrastructure.Repositories
{
    using System.Diagnostics.CodeAnalysis;

    using Ardalis.Specification;
    using Ardalis.Specification.EntityFrameworkCore;

    using GamaEdtech.Domain;
    using GamaEdtech.Domain.Repositories;
    using GamaEdtech.Infrastructure.EntityFramework.Context;

    using Microsoft.EntityFrameworkCore;

    public abstract class BaseRepository<TContext, TEntity>(TContext dbContext) : RepositoryBase<TEntity>(dbContext)
        where TContext : DbContext
        where TEntity : class, IAggregateRoot
    {
        protected TContext Context => dbContext;

        public override Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default)
            => base.AddAsync(entity, cancellationToken);

        public override Task<IEnumerable<TEntity>> AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
            => base.AddRangeAsync(entities, cancellationToken);

        public override Task<bool> AnyAsync(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)
            => base.AnyAsync(specification, cancellationToken);

        public override Task<bool> AnyAsync(CancellationToken cancellationToken = default)
            => base.AnyAsync(cancellationToken);

        public override Task<int> CountAsync(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)
            => base.CountAsync(specification, cancellationToken);

        public override Task<int> CountAsync(CancellationToken cancellationToken = default)
            => base.CountAsync(cancellationToken);

        public override Task<int> DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
            => base.DeleteAsync(entity, cancellationToken);

        public override Task<int> DeleteRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
            => base.DeleteRangeAsync(entities, cancellationToken);

        public override Task<TEntity?> FirstOrDefaultAsync([NotNull] ISpecification<TEntity> specification, CancellationToken cancellationToken = default)
            => base.FirstOrDefaultAsync(specification, cancellationToken);

        public override Task<TResult?> FirstOrDefaultAsync<TResult>(ISpecification<TEntity, TResult> specification, CancellationToken cancellationToken = default)
            where TResult : default
            => base.FirstOrDefaultAsync(specification, cancellationToken);

        public override Task<TEntity?> GetByIdAsync<TId>(TId id, CancellationToken cancellationToken = default)
            => base.GetByIdAsync(id, cancellationToken);

        public override Task<List<TEntity>> ListAsync(CancellationToken cancellationToken = default)
            => base.ListAsync(cancellationToken);

        public override Task<List<TEntity>> ListAsync(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)
            => base.ListAsync(specification, cancellationToken);

        public override Task<List<TResult>> ListAsync<TResult>(ISpecification<TEntity, TResult> specification, CancellationToken cancellationToken = default)
            => base.ListAsync(specification, cancellationToken);

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => base.SaveChangesAsync(cancellationToken);

        public override Task<TEntity?> SingleOrDefaultAsync(ISingleResultSpecification<TEntity> specification, CancellationToken cancellationToken = default)
            => base.SingleOrDefaultAsync(specification, cancellationToken);

        public override Task<TResult?> SingleOrDefaultAsync<TResult>(ISingleResultSpecification<TEntity, TResult> specification, CancellationToken cancellationToken = default) where TResult : default
            => base.SingleOrDefaultAsync(specification, cancellationToken);

        public override Task<int> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
            => base.UpdateAsync(entity, cancellationToken);

        public override Task<int> UpdateRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
            => base.UpdateRangeAsync(entities, cancellationToken);

        protected override IQueryable<TEntity> ApplySpecification(ISpecification<TEntity> specification, bool evaluateCriteriaOnly = false)
            => base.ApplySpecification(specification, evaluateCriteriaOnly);

        protected override IQueryable<TResult> ApplySpecification<TResult>(ISpecification<TEntity, TResult> specification)
            => base.ApplySpecification(specification);
    }

    public class BaseRepository<TEntity>(ApplicationDBContext dbContext) : BaseRepository<ApplicationDBContext, TEntity>(dbContext),
        IBaseRepository<TEntity>
        where TEntity : class, IAggregateRoot
    {
        public override Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default)
            => base.AddAsync(entity, cancellationToken);

        public override Task<IEnumerable<TEntity>> AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
            => base.AddRangeAsync(entities, cancellationToken);

        public override Task<bool> AnyAsync(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)
            => base.AnyAsync(specification, cancellationToken);

        public override Task<bool> AnyAsync(CancellationToken cancellationToken = default)
            => base.AnyAsync(cancellationToken);

        public override Task<int> CountAsync(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)
            => base.CountAsync(specification, cancellationToken);

        public override Task<int> CountAsync(CancellationToken cancellationToken = default)
            => base.CountAsync(cancellationToken);

        public override Task<int> DeleteAsync(TEntity entity, CancellationToken cancellationToken = default)
            => base.DeleteAsync(entity, cancellationToken);

        public override Task<int> DeleteRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
            => base.DeleteRangeAsync(entities, cancellationToken);

        public override Task<TResult?> FirstOrDefaultAsync<TResult>(ISpecification<TEntity, TResult> specification, CancellationToken cancellationToken = default)
            where TResult : default
            => base.FirstOrDefaultAsync(specification, cancellationToken);

        public override Task<TEntity?> GetByIdAsync<TId>(TId id, CancellationToken cancellationToken = default)
            => base.GetByIdAsync(id, cancellationToken);

        public override Task<List<TEntity>> ListAsync(CancellationToken cancellationToken = default)
            => base.ListAsync(cancellationToken);

        public override Task<List<TEntity>> ListAsync(ISpecification<TEntity> specification, CancellationToken cancellationToken = default)
            => base.ListAsync(specification, cancellationToken);

        public override Task<List<TResult>> ListAsync<TResult>(ISpecification<TEntity, TResult> specification, CancellationToken cancellationToken = default)
            => base.ListAsync(specification, cancellationToken);

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => base.SaveChangesAsync(cancellationToken);

        public override Task<TEntity?> SingleOrDefaultAsync(ISingleResultSpecification<TEntity> specification, CancellationToken cancellationToken = default)
            => base.SingleOrDefaultAsync(specification, cancellationToken);

        public override Task<TResult?> SingleOrDefaultAsync<TResult>(ISingleResultSpecification<TEntity, TResult> specification, CancellationToken cancellationToken = default)
            where TResult : default
            => base.SingleOrDefaultAsync(specification, cancellationToken);

        public override Task<int> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
            => base.UpdateAsync(entity, cancellationToken);

        public override Task<int> UpdateRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
            => base.UpdateRangeAsync(entities, cancellationToken);

        protected override IQueryable<TEntity> ApplySpecification(ISpecification<TEntity> specification, bool evaluateCriteriaOnly = false)
            => base.ApplySpecification(specification, evaluateCriteriaOnly);

        protected override IQueryable<TResult> ApplySpecification<TResult>(ISpecification<TEntity, TResult> specification)
            => base.ApplySpecification(specification);
    }
}
