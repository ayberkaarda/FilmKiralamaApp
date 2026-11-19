using System.Net;
using System.Net.Sockets;

namespace FilmKiralama.UI.Infrastructure;

/// <summary>
/// Which addresses a poster may be downloaded from. Poster addresses come from the database, and
/// every customer's computer downloads them from its own network, so only public web addresses are
/// allowed: no other scheme, and no loopback, private, link-local (cloud metadata), shared, multicast
/// or reserved address, also not hidden in an IPv6 form.
/// </summary>
internal static class UrlGuard
{
    /// <summary>An absolute http or https address without user information.</summary>
    public static bool IsAllowedUri(Uri? uri) =>
        uri is { IsAbsoluteUri: true }
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && string.IsNullOrEmpty(uri.UserInfo)
        && !string.IsNullOrEmpty(uri.Host);

    /// <summary>Whether <paramref name="address"/> is a public unicast address.</summary>
    public static bool IsPublicAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return false;
        }

        var b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return !(b[0] == 0                                  // "this" network
                || b[0] == 10                                    // private
                || b[0] == 127                                   // loopback
                || (b[0] == 100 && (b[1] & 0xC0) == 64)          // shared address space (carrier NAT)
                || (b[0] == 169 && b[1] == 254)                  // link-local, cloud metadata
                || (b[0] == 172 && (b[1] & 0xF0) == 16)          // private
                || (b[0] == 192 && b[1] == 0 && b[2] == 0)       // protocol assignments
                || (b[0] == 192 && b[1] == 168)                  // private
                || (b[0] == 198 && (b[1] & 0xFE) == 18)          // benchmarking
                || b[0] >= 224);                                 // multicast, reserved, broadcast
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return false;
        }

        if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || address.IsIPv6UniqueLocal)
        {
            return false;
        }

        // ::/96 (unspecified, loopback, IPv4-compatible) and the NAT64 prefix 64:ff9b::/96 carry an IPv4 address.
        if (b.AsSpan(0, 12).IndexOfAnyExcept((byte)0) < 0
            || (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B && b.AsSpan(4, 8).IndexOfAnyExcept((byte)0) < 0))
        {
            return IsPublicAddress(new IPAddress(b.AsSpan(12, 4)));
        }

        // 6to4 (2002::/16) carries an IPv4 address; Teredo (2001:0::/32) tunnels to any.
        if (b[0] == 0x20 && b[1] == 0x02)
        {
            return IsPublicAddress(new IPAddress(b.AsSpan(2, 4)));
        }

        return !(b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x00 && b[3] == 0x00);
    }
}
