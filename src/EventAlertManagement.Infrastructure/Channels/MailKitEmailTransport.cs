using System.Net.Sockets;
using EventAlertManagement.Core.Contracts;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace EventAlertManagement.Infrastructure.Channels;

public sealed class MailKitEmailTransport(IOptions<SmtpOptions> options) : IEmailTransport
{
    public async Task<ChannelSendResult> SendAsync(MimeMessage message, CancellationToken ct)
    {
        var o = options.Value;
        try
        {
            using var client = new SmtpClient();
            client.Timeout = 15_000;
            await client.ConnectAsync(o.Host, o.Port,
                o.UseTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None, ct);
            if (!string.IsNullOrEmpty(o.Username))
                await client.AuthenticateAsync(o.Username, o.Password ?? "", ct);
            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);
            return ChannelSendResult.Ok();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (SmtpCommandException ex) when ((int)ex.StatusCode is >= 400 and < 500)
        {
            return ChannelSendResult.Transient($"SMTP server asked to try later ({(int)ex.StatusCode}).");
        }
        catch (SmtpCommandException ex)
        {
            return ChannelSendResult.Permanent($"SMTP server rejected the message ({(int)ex.StatusCode}).");
        }
        catch (AuthenticationException)
        {
            return ChannelSendResult.Transient("SMTP authentication failed (server configuration).");
        }
        catch (Exception ex) when (ex is SocketException or IOException or TimeoutException
                                     or ServiceNotConnectedException or ProtocolException or OperationCanceledException)
        {
            return ChannelSendResult.Transient("Could not reach the SMTP server.");
        }
    }
}
