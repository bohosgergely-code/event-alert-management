using System.Net;
using System.Net.Sockets;

namespace EventAlertManagement.Infrastructure.Security;

/// <summary>The destination points into a private or internal network.</summary>
public sealed class BlockedAddressException(string message) : Exception(message);

/// <summary>
/// Server-side request forgery protection for destinations the user controls.
/// The check happens when the connection is made, on the address that is actually used. Checking the host name
/// earlier is not enough: a name can answer with a public address when it is validated and an internal one a
/// moment later ("DNS rebinding").
/// </summary>
public static class SsrfGuard
{
    /// <summary>Returns the addresses if every one of them is allowed. One internal address among several is enough to refuse.</summary>
    public static IReadOnlyList<IPAddress> RequireAllowed(IReadOnlyList<IPAddress> addresses, Func<IPAddress, bool>? isAllowed = null)
    {
        isAllowed ??= PublicAddressPolicy.IsPublic;
        if (addresses.Count == 0) throw new BlockedAddressException("The host name has no address.");
        if (addresses.Any(a => !isAllowed(a))) throw new BlockedAddressException("The address points to a private or internal network.");
        return addresses;
    }

    /// <summary>A connect callback for <see cref="SocketsHttpHandler"/> that resolves, checks and then connects to the checked address.</summary>
    public static Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>> CreateConnectCallback(
        Func<string, CancellationToken, Task<IPAddress[]>>? resolver = null, Func<IPAddress, bool>? isAllowed = null)
    {
        resolver ??= (host, ct) => Dns.GetHostAddressesAsync(host, ct);

        return async (context, ct) =>
        {
            var endpoint = context.DnsEndPoint;
            var host = endpoint.Host;
            var addresses = IPAddress.TryParse(host, out var literal) ? new[] { literal } : await resolver(host, ct);
            RequireAllowed(addresses, isAllowed);

            Exception? last = null;
            foreach (var address in addresses)
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(address, endpoint.Port), ct).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true); // TLS is still negotiated for the original host name
                }
                catch (Exception ex) when (ex is SocketException)
                {
                    socket.Dispose();
                    last = ex;
                }
                catch { socket.Dispose(); throw; }
            }
            throw last ?? new SocketException((int)SocketError.HostNotFound);
        };
    }
}
