using System.Net;
using System.Net.Sockets;

namespace EventAlertManagement.Infrastructure.Security;

/// <summary>
/// Decides whether an IP address belongs to the public internet. Anything that points inside a network
/// (loopback, private ranges, link-local such as cloud metadata at 169.254.169.254, documentation and
/// reserved ranges, multicast) is refused. IPv6 uses an allow-list (global unicast only), IPv4 a block-list.
/// </summary>
public static class PublicAddressPolicy
{
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4(); // ::ffff:127.0.0.1 is just 127.0.0.1

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsPublicV4(address.GetAddressBytes()),
            AddressFamily.InterNetworkV6 => IsPublicV6(address.GetAddressBytes()),
            _ => false,
        };
    }

    private static bool IsPublicV4(byte[] b)
    {
        var (a, c) = (b[0], b[1]);
        return !(
            a == 0                                   // "this network", 0.0.0.0
            || a == 10                               // private
            || (a == 100 && c is >= 64 and <= 127)   // carrier-grade NAT
            || a == 127                              // loopback
            || (a == 169 && c == 254)                // link-local, cloud metadata
            || (a == 172 && c is >= 16 and <= 31)    // private
            || (a == 192 && c == 0 && b[2] is 0 or 2)       // protocol assignments, documentation
            || (a == 192 && c == 88 && b[2] == 99)   // 6to4 relay
            || (a == 192 && c == 168)                // private
            || (a == 198 && c is 18 or 19)           // benchmarking
            || (a == 198 && c == 51 && b[2] == 100)  // documentation
            || (a == 203 && c == 0 && b[2] == 113)   // documentation
            || a >= 224);                            // multicast, reserved, broadcast
    }

    private static bool IsPublicV6(byte[] b)
    {
        // Only global unicast (2000::/3) can be public.
        if ((b[0] & 0xE0) != 0x20) return false;

        // Reserved blocks inside 2000::/3.
        if (b[0] == 0x20 && b[1] == 0x01 && (b[2] & 0xFE) == 0x00) return false;                 // 2001::/23 protocol assignments (Teredo, ORCHID)
        if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0d && b[3] == 0xb8) return false;           // 2001:db8::/32 documentation
        if (b[0] == 0x20 && b[1] == 0x02) return false;                                           // 2002::/16 6to4 (embeds an IPv4 address)
        if (b[0] == 0x3f && b[1] == 0xff && (b[2] & 0xF0) == 0x00) return false;                 // 3fff::/20 documentation
        return true;
    }
}
