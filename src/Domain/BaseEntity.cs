namespace GamaEdtech.Domain
{
    using GamaEdtech.Common.Core.Extensions;

    public interface IEntity
    {
        DateTime CreateDate { get; }
        public bool SoftDeleted { get; }
        DateTime LastUpdatedDate { get; }
    }
    public abstract class BaseEntity<TKey> : IEntity
    {
        protected BaseEntity()
        {
            CreateDate = DateTimeHelper.SystemNow();
            SoftDeleted = false;
        }

        public virtual TKey Id { get; protected set; }
        public virtual DateTime CreateDate { get; private set; }
        public virtual bool SoftDeleted { get; private set; }
        public virtual DateTime LastUpdatedDate { get; private set; }

        public void UpdateLastUpdatedDate() => LastUpdatedDate = DateTimeHelper.SystemNow();
    }

    public abstract class BaseEntity : BaseEntity<Guid>
    {
    }
}
