namespace GamaEdtech.Test.Core
{
    using GamaEdtech.Common.Core;

    using Microsoft.AspNetCore.Http;

    using Xunit;

    /// <summary>
    /// The IP a cookie sign-in is bound to. Behind Cloudflare the last X-Forwarded-For entry is the edge server, which
    /// changes between requests, so it must come from CF-Connecting-IP when that holds a real IP (2026-10-19).
    /// </summary>
    public class DeviceBindingIpAddressTests
    {
        private static DefaultHttpContext Request(string? xForwardedFor = null, string? cfConnectingIp = null)
        {
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
            if (xForwardedFor is not null)
            {
                context.Request.Headers["X-Forwarded-For"] = xForwardedFor;
            }

            if (cfConnectingIp is not null)
            {
                context.Request.Headers["CF-Connecting-IP"] = cfConnectingIp;
            }

            return context;
        }

        [Fact]
        public void SameClientThroughDifferentEdgesBindsToTheSameIp()
        {
            var first = Request("203.0.113.7, 172.68.10.1", "203.0.113.7");
            var second = Request("203.0.113.7, 162.158.40.9", "203.0.113.7");

            Assert.Equal("203.0.113.7", first.GetDeviceBindingIpAddress());
            Assert.Equal(first.GetDeviceBindingIpAddress(), second.GetDeviceBindingIpAddress());
        }

        [Fact]
        public void Ipv6ClientIsKeptWhole() =>
            Assert.Equal("2001:db8::1", Request("2001:db8::1, 172.68.10.1", " 2001:db8::1 ").GetDeviceBindingIpAddress());

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not-an-ip")]
        public void WithoutAUsableHeaderItFallsBackToTheUsualClientIp(string? cfConnectingIp)
        {
            var context = Request("198.51.100.4, 10.0.0.2", cfConnectingIp);

            Assert.Equal(context.GetClientIpAddress(), context.GetDeviceBindingIpAddress());
        }

        // Login history, audit and the IP sent to gama-api keep this as it was: the last entry, untrimmed.
        [Fact]
        public void TheUsualClientIpIsUnchanged() =>
            Assert.Equal(" 172.68.10.1", Request("203.0.113.7, 172.68.10.1", "203.0.113.7").GetClientIpAddress());
    }
}
