namespace GamaEdtech.Domain
{
    using System.Text.Json.Serialization;

    public abstract class AggregateRoot<T> : BaseEntity<T>, IAggregateRoot
    {
        protected AggregateRoot() : base() => domainEvents = [];

        [JsonIgnore]
        private readonly List<IDomainEvent> domainEvents;

        [JsonIgnore]
        public IReadOnlyList<IDomainEvent> DomainEvents => domainEvents;

        protected void AddDomainEvent(IDomainEvent domainEvent)
        {
            if (domainEvent == null)
            {
                return;
            }

            domainEvents?.Add(domainEvent);
        }

        protected void RemoveDomainEvent(IDomainEvent domainEvent)
        {
            if (domainEvent == null)
            {
                return;
            }

            _ = domainEvents?.Remove(domainEvent);
        }

        public void ClearDomainEvents() => domainEvents?.Clear();
    }

    public abstract class AggregateRoot : AggregateRoot<Guid>
    {

    }
}
