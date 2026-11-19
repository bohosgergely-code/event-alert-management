using EventAlertManagement.Core.Contracts;

namespace EventAlertManagement.Infrastructure.Channels;

/// <summary>Finds channel implementations by type. Registering a new <see cref="INotificationChannel"/> is all it takes to add a channel.</summary>
public sealed class ChannelRegistry : IChannelRegistry
{
    private readonly Dictionary<string, INotificationChannel> _channels;

    public ChannelRegistry(IEnumerable<INotificationChannel> channels)
    {
        _channels = new(StringComparer.OrdinalIgnoreCase);
        foreach (var channel in channels)
        {
            if (!_channels.TryAdd(channel.Type, channel))
                throw new InvalidOperationException($"Channel type '{channel.Type}' is registered more than once.");
        }
    }

    public INotificationChannel? Find(string type) => _channels.GetValueOrDefault(type);
    public IReadOnlyCollection<string> Types => _channels.Keys;
}
