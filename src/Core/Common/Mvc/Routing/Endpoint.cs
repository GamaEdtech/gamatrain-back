namespace GamaEdtech.Common.Mvc.Routing
{
    using System.Collections.Generic;

    public struct Endpoint
    {
        public string? EndpointName { get; set; }

        public KeyValuePair<string, string>? Area { get; set; }

        public KeyValuePair<string, string>? Action { get; set; }

        public KeyValuePair<string, string>? Controller { get; set; }

        /// <summary>Whether a per-user grant can open this endpoint (<see cref="Identity.DelegablePermissionAttribute"/>).</summary>
        public bool Delegable { get; set; }
    }
}
