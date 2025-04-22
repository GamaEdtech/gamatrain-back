namespace GamaEdtech.Domain
{
    public interface IAggregateRoot : IEntity
    {
        void ClearDomainEvents();
        IReadOnlyList<IDomainEvent> DomainEvents { get; }
    }
}
