namespace GamaEdtech.Common.HttpProvider
{
    using System;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// The named <see cref="HttpClient"/> for links given by users (e.g. an image link an AI assistant hands over): it
    /// connects only to public addresses, checked on the address actually connected to, so neither a DNS answer nor a
    /// redirect can reach this server's own network (SSRF). No proxy, at most 3 redirects.
    /// </summary>
    public static class PublicNetworkHttpClient
    {
        public const string Name = "PublicNetwork";

        public static SocketsHttpHandler CreateHandler() => new()
        {
            UseProxy = false,
            MaxAutomaticRedirections = 3,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            ConnectCallback = ConnectToPublicAddressAsync,
        };

        private static async ValueTask<Stream> ConnectToPublicAddressAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
            var address = Array.Find(addresses, IsPublicAddress) ?? throw new HttpRequestException("The link points to a private network address.");
#pragma warning disable CA2000 // Dispose objects before losing scope: the NetworkStream owns it, and it is disposed below on failure
            Socket socket = new(SocketType.Stream, ProtocolType.Tcp);
#pragma warning restore CA2000 // Dispose objects before losing scope
            try
            {
                socket.NoDelay = true;
                await socket.ConnectAsync(address, context.DnsEndPoint.Port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception)
            {
                socket.Dispose();
                throw;
            }
        }

        private static bool IsPublicAddress(IPAddress address)
        {
            var ip = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
            if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any))
            {
                return false;
            }

            if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                return !(ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal || ip.IsIPv6Multicast);
            }

            var bytes = ip.GetAddressBytes();
            return bytes[0] switch
            {
                0 or 10 or 127 or >= 224 => false,
                100 => bytes[1] is < 64 or > 127,
                169 => bytes[1] != 254,
                172 => bytes[1] is < 16 or > 31,
                192 => bytes[1] != 168 && !(bytes[1] == 0 && bytes[2] is 0 or 2),
                198 => bytes[1] is not (18 or 19),
                _ => true,
            };
        }
    }
}
