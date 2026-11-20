using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace SchoolLibrary.Services.SiteIcons;

/// <summary>
/// Keeps requests to administrator-typed addresses on the public internet (protection against SSRF).
/// The check runs when the connection is opened, after the name is resolved, so it also covers
/// redirects and names that resolve to an internal address (localhost, the private network, the
/// cloud metadata service at 169.254.169.254).
/// </summary>
public static class PublicNetworkGuard
{
    /// <summary>Only the standard web ports: a redirect cannot reach SSH, the database and the like.</summary>
    public static readonly IReadOnlySet<int> AllowedPorts = new HashSet<int> { 80, 443 };

    /// <summary>A handler whose every connection goes through <see cref="ConnectAsync"/>.</summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        // A proxy would make the guard check the proxy instead of the real target
        UseProxy = false,
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 3,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        AutomaticDecompression = DecompressionMethods.All,
        ConnectCallback = ConnectAsync
    };

    /// <summary>Opens the connection only when the port is a web port and every address of the host is public.</summary>
    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var endPoint = context.DnsEndPoint;
        if (!AllowedPorts.Contains(endPoint.Port))
        {
            throw new HttpRequestException($"Port {endPoint.Port} is not allowed.");
        }

        var addresses = IPAddress.TryParse(endPoint.Host, out var literal)
            ? new[] { literal }
            : await Dns.GetHostAddressesAsync(endPoint.Host, cancellationToken);

        // A host with even one internal address is refused: the connection could pick that one
        if (addresses.Length == 0 || !addresses.All(IsPublic))
        {
            throw new HttpRequestException($"The host {endPoint.Host} is not on the public internet.");
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, endPoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>True for an address on the public internet; false for loopback, private, link-local, reserved and similar ranges.</summary>
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsPublicV4(address.GetAddressBytes()),
            AddressFamily.InterNetworkV6 => IsPublicV6(address),
            _ => false
        };
    }

    private static bool IsPublicV4(byte[] b) => !(
        b[0] == 0                                        // "this network"
        || b[0] == 10                                    // private
        || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)    // carrier-grade NAT
        || b[0] == 127                                   // loopback
        || (b[0] == 169 && b[1] == 254)                  // link-local, cloud metadata
        || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)     // private
        || (b[0] == 192 && b[1] == 0 && b[2] == 0)       // protocol assignments
        || (b[0] == 192 && b[1] == 0 && b[2] == 2)       // documentation
        || (b[0] == 192 && b[1] == 168)                  // private
        || (b[0] == 198 && (b[1] == 18 || b[1] == 19))   // benchmarking
        || (b[0] == 198 && b[1] == 51 && b[2] == 100)    // documentation
        || (b[0] == 203 && b[1] == 0 && b[2] == 113)     // documentation
        || b[0] >= 224);                                 // multicast, reserved, broadcast

    private static bool IsPublicV6(IPAddress address)
    {
        if (address.Equals(IPAddress.IPv6Loopback) || address.Equals(IPAddress.IPv6Any)
            || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast
            || address.IsIPv6Teredo || address.IsIPv6UniqueLocal)
        {
            return false;
        }

        var b = address.GetAddressBytes();

        // Only global unicast (2000::/3) is public
        if ((b[0] & 0xE0) != 0x20) return false;

        // 6to4 (2002::/16) carries an IPv4 address that could be internal; 2001:db8::/32 is documentation
        if (b[0] == 0x20 && b[1] == 0x02) return false;
        if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0d && b[3] == 0xb8) return false;

        return true;
    }
}
