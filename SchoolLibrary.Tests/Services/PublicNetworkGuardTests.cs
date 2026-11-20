using System.Net;
using SchoolLibrary.Services.SiteIcons;

namespace SchoolLibrary.Tests.Services;

public class PublicNetworkGuardTests
{
    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("93.184.216.34")]
    [InlineData("172.32.0.1")]
    [InlineData("100.128.0.1")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("::ffff:8.8.8.8")]
    public void IsPublic_AcceptsPublicAddresses(string address)
    {
        Assert.True(PublicNetworkGuard.IsPublic(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("127.0.0.1")]          // loopback
    [InlineData("10.0.0.5")]           // private
    [InlineData("172.16.0.1")]         // private
    [InlineData("172.31.255.255")]     // private
    [InlineData("192.168.1.1")]        // private
    [InlineData("169.254.169.254")]    // cloud metadata
    [InlineData("100.64.0.1")]         // carrier-grade NAT
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]          // multicast
    [InlineData("255.255.255.255")]
    [InlineData("192.0.2.10")]         // documentation
    [InlineData("198.18.0.1")]         // benchmarking
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fe80::1")]            // link-local
    [InlineData("fd00::1")]            // unique local
    [InlineData("ff02::1")]            // multicast
    [InlineData("::ffff:127.0.0.1")]   // IPv4-mapped loopback
    [InlineData("::ffff:10.1.2.3")]
    [InlineData("2002:7f00:1::")]      // 6to4 with 127.0.0.1 inside
    [InlineData("2001:db8::1")]        // documentation
    [InlineData("64:ff9b::7f00:1")]    // NAT64
    public void IsPublic_RefusesInternalAndReservedAddresses(string address)
    {
        Assert.False(PublicNetworkGuard.IsPublic(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://localhost/")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("https://[::1]/")]
    public async Task TheHandler_DoesNotConnectToInternalHosts(string url)
    {
        using var http = new HttpClient(PublicNetworkGuard.CreateHandler());

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync(url));

        Assert.Contains("public internet", error.ToString());
    }

    [Fact]
    public async Task TheHandler_DoesNotConnectToOtherPorts()
    {
        using var http = new HttpClient(PublicNetworkGuard.CreateHandler());

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync("http://8.8.8.8:22/"));

        Assert.Contains("Port 22", error.ToString());
    }
}
