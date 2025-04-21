namespace GamaEdtech.Domain
{
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
            CreateDate = DateTime.Now;
            SoftDeleted = false;
        }

        public virtual TKey Id { get; protected set; }
        public virtual DateTime CreateDate { get; private set; }
        public virtual bool SoftDeleted { get; private set; }
        public virtual DateTime LastUpdatedDate { get; private set; }

        public void UpdateLastUpdatedDate() => LastUpdatedDate = DateTime.Now;
    }

    public abstract class BaseEntity : BaseEntity<Guid>
    {
    }
}
