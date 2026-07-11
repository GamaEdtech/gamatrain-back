namespace GamaEdtech.Data.Dto.Connection
{
    public sealed class ConnectionStatusRequestDto
    {
        public required long UserId { get; set; }
        public required IEnumerable<long> TargetIds { get; set; }
    }
}
