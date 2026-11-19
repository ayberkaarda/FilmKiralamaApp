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

        // Forms that carry an IPv4 address in their last 32 bits are judged by that address: ::/96
        // (unspecified, loopback, IPv4-compatible ::a.b.c.d), the IPv4-translated ::ffff:0:a.b.c.d
        // (SIIT) and the well-known NAT64 prefix 64:ff9b::/96. IPv4-mapped addresses were mapped above.
        if (IsZero(b, 0, 12) || (IsZero(b, 0, 8) && b[8] == 0xFF && b[9] == 0xFF && IsZero(b, 10, 2)) || IsNat64(b))
        {
            return IsPublicAddress(new IPAddress(b.AsSpan(12, 4)));
        }

        // The local-use NAT64 prefix 64:ff9b:1::/48 translates to addresses of the local network only.
        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B && b[4] == 0x00 && b[5] == 0x01)
        {
            return false;
        }

        // 6to4 (2002::/16) carries an IPv4 address in bits 16 to 47.
        if (b[0] == 0x20 && b[1] == 0x02)
        {
            return IsPublicAddress(new IPAddress(b.AsSpan(2, 4)));
        }

        // Teredo (2001:0::/32) tunnels to any address; documentation (2001:db8::/32) and discard-only
        // (100::/64) addresses are never real servers.
        return !((b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x00 && b[3] == 0x00)
            || (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8)
            || (b[0] == 0x01 && b[1] == 0x00 && IsZero(b, 2, 6)));
    }

    /// <summary>The well-known NAT64 prefix 64:ff9b::/96.</summary>
    private static bool IsNat64(byte[] b) =>
        b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B && IsZero(b, 4, 8);

    private static bool IsZero(byte[] bytes, int start, int length) =>
        bytes.AsSpan(start, length).IndexOfAnyExcept((byte)0) < 0;
}
