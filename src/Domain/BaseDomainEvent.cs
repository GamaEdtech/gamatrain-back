namespace GamaEdtech.Domain
{
    public interface IDomainEvent
    {
        public Guid EventId { get; }
        public string Route { get; }
    }
}
