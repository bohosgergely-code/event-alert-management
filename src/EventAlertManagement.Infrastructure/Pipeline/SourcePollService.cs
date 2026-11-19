using EventAlertManagement.Core.Contracts;
using EventAlertManagement.Core.Domain;
using EventAlertManagement.Infrastructure.Persistence;
using EventAlertManagement.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EventAlertManagement.Infrastructure.Pipeline;

public sealed record PollResult(bool Success, int NewEvents, string? Error);

/// <summary>Polls sources on schedule, tracks their health, and hands events to ingestion.</summary>
public sealed class SourcePollService(
    AppDbContext db, IEnumerable<IEventSource> adapters, EventIngestionService ingestion,
    IClock clock, ILogger<SourcePollService> log)
{
    public async Task<int> PollDueAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var sources = await db.Sources.AsNoTracking().Where(s => s.IsEnabled).ToListAsync(ct);
        var polled = 0;

        foreach (var source in sources)
        {
            var due = source.LastPollAt is null || now >= source.LastPollAt.Value.AddSeconds(source.PollIntervalSeconds);
            if (!due) continue;
            await PollAsync(source.Id, ct);
            polled++;
        }
        return polled;
    }

    /// <summary>Polls one source. Each poll is its own unit of work, so a failure never affects other sources.</summary>
    public async Task<PollResult> PollAsync(Guid sourceId, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var source = await db.Sources.FirstAsync(s => s.Id == sourceId, ct);
        var now = clock.UtcNow;
        var adapter = adapters.FirstOrDefault(a => string.Equals(a.Key, source.Key, StringComparison.OrdinalIgnoreCase));
        if (adapter is null)
            return await RecordFailureAsync(source, now, "No adapter is registered for this source.", ct);

        try
        {
            var fetched = await adapter.FetchAsync(source.Cursor, ct);

            // The cursor moves forward only together with the stored events (same SaveChanges).
            source.LastPollAt = now;
            source.LastSuccessAt = now;
            source.LastError = null;
            source.ConsecutiveFailures = 0;
            var result = await ingestion.IngestAsync(source, fetched.Events, fetched.NewCursor, ct);
            return new PollResult(true, result.NewEvents, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Polling {Source} failed", source.Key);
            db.ChangeTracker.Clear(); // Discard half-built events and notifications from the failed attempt.
            var fresh = await db.Sources.FirstAsync(s => s.Id == sourceId, ct);
            return await RecordFailureAsync(fresh, now, $"{ex.GetType().Name}: {ErrorSanitizer.Sanitize(ex.Message)}", ct);
        }
    }

    private async Task<PollResult> RecordFailureAsync(Source source, DateTime now, string error, CancellationToken ct)
    {
        source.LastPollAt = now;
        source.LastError = ErrorSanitizer.Sanitize(error);
        source.ConsecutiveFailures++;
        await db.SaveChangesAsync(ct);
        return new PollResult(false, 0, source.LastError);
    }
}
