namespace GamaEdtech.Presentation.ViewModel.Connection
{
    public sealed class ConnectionStatusResponseViewModel
    {
        /// <summary>
        /// Echoes back the id exactly as requested (local Id or CoreId, matching the request's IdType) so the
        /// caller can match results back to its own list without knowing the resolved local id.
        /// </summary>
        public long Id { get; set; }

        public bool IsFollowing { get; set; }
    }
}
