using System.Collections.Concurrent;
using EventAlertManagement.Core.Contracts;
using EventAlertManagement.Core.Domain;

namespace EventAlertManagement.Infrastructure.Sources;

/// <summary>
/// Produces events on demand (admin injection, demos, tests). It uses the same contract and the same
/// pipeline as real sources, so simulated events exercise the real path.
/// </summary>
public sealed class SimulatorSource : IEventSource
{
    public const string SourceKey = "simulator";
    private readonly ConcurrentQueue<NormalizedEvent> _queue = new();

    public string Key => SourceKey;
    public EventCategory? Category => null;

    public void Enqueue(NormalizedEvent ev) => _queue.Enqueue(ev with { IsSimulated = true });

    public Task<SourceFetchResult> FetchAsync(string? cursor, CancellationToken ct)
    {
        var events = new List<NormalizedEvent>();
        while (_queue.TryDequeue(out var ev)) events.Add(ev);
        return Task.FromResult(new SourceFetchResult(events, cursor));
    }
}
