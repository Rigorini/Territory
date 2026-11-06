using System.Net;
using System.Net.Sockets;

namespace Territory.Application.Security;

/// <summary>
/// Identifies the network a request comes from, for per-client login lockout. An IPv6 client usually owns a whole /64
/// and can pick a new address for every request, so IPv6 addresses are reduced to that prefix.
/// </summary>
public static class ClientNetwork
{
    /// <summary>Used when the address is unknown (e.g. in-memory test server): all such clients share one key.</summary>
    public const string Unknown = "unknown";

    public static string KeyOf(IPAddress? address)
    {
        if (address is null)
        {
            return Unknown;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes) + "/64";
    }
}
