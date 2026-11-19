using System.Collections.Concurrent;
using EventAlertManagement.Core.Contracts;
using EventAlertManagement.Core.Domain;
using EventAlertManagement.Core.Matching;
using EventAlertManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EventAlertManagement.Infrastructure.Pipeline;

public sealed record IngestResult(int NewEvents, int Duplicates, int NotificationsCreated, int NotificationsSuppressed);

/// <summary>
/// Stores new events and turns them into notifications. Everything happens in one SaveChanges, so a crash
/// cannot leave an event that was stored but never matched (ADR-011).
/// </summary>
public sealed class EventIngestionService(
    AppDbContext db, IEventMatcher matcher, IClock clock, ILogger<EventIngestionService> log)
{
    // One ingest per source at a time inside this process. Without it, an admin pressing "Check now" while the
    // background poll runs makes both read "not stored yet" and one of them fails on the unique key.
    // (Several instances would need the same guarantee from the database.)
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> SourceLocks = new();

    public async Task<IngestResult> IngestAsync(
        Source source, IReadOnlyList<NormalizedEvent> items, string? newCursor, CancellationToken ct)
    {
        var gate = SourceLocks.GetOrAdd(source.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try { return await IngestLockedAsync(source, items, newCursor, ct); }
        finally { gate.Release(); }
    }

    private async Task<IngestResult> IngestLockedAsync(
        Source source, IReadOnlyList<NormalizedEvent> items, string? newCursor, CancellationToken ct)
    {
        var now = clock.UtcNow;

        // De-duplicate within the batch and against what is already stored.
        var candidates = items
            .GroupBy(i => DedupeKey(source, i))
            .Select(g => (Key: g.Key, Item: g.First()))
            .ToList();
        var keys = candidates.Select(c => c.Key).ToList();
        var existing = (await db.Events.Where(e => keys.Contains(e.DedupeKey)).Select(e => e.DedupeKey).ToListAsync(ct)).ToHashSet();

        var fresh = candidates
            .Where(c => !existing.Contains(c.Key))
            .OrderBy(c => c.Item.OccurredAt)
            .Select(c => ToEntity(source, c.Key, c.Item, now))
            .ToList();
        var duplicates = candidates.Count - fresh.Count;

        db.Events.AddRange(fresh);

        int created = 0, suppressed = 0;
        if (fresh.Count > 0)
        {
            var categories = fresh.Select(e => e.Category).Distinct().ToList();
            var rules = await db.AlertRules
                .Include(r => r.Channels)
                .Where(r => r.IsEnabled && categories.Contains(r.Category))
                .ToListAsync(ct);

            var disabledTypes = (await db.ChannelTypeSettings.Where(t => !t.IsEnabled).Select(t => t.Type).ToListAsync(ct))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Most severe first: when several events arrive together and a cooldown applies, the one that gets through
            // must be the one that matters most, not merely the one that happened first.
            foreach (var ev in fresh.OrderByDescending(e => e.Severity).ThenBy(e => e.OccurredAt))
            {
                foreach (var rule in rules.Where(r => matcher.Matches(r, ev)))
                {
                    var inCooldown = CooldownPolicy.IsInCooldown(rule, now, out var until, ev.Severity);
                    var queuedForRule = false;

                    foreach (var channel in rule.Channels)
                    {
                        var n = new Notification
                        {
                            RuleId = rule.Id, EventId = ev.Id, ChannelConfigId = channel.Id, CreatedAt = now,
                        };

                        if (!channel.IsEnabled)
                        {
                            n.Status = NotificationStatus.Suppressed;
                            n.SuppressedReason = "Channel is disabled.";
                        }
                        else if (disabledTypes.Contains(channel.Type))
                        {
                            n.Status = NotificationStatus.Suppressed;
                            n.SuppressedReason = $"{channel.Type} notifications are switched off by an administrator.";
                        }
                        else if (inCooldown)
                        {
                            n.Status = NotificationStatus.Suppressed;
                            n.SuppressedReason = $"Rule cooldown until {until:u}.";
                        }
                        else
                        {
                            n.Status = NotificationStatus.Pending;
                            n.NextAttemptAt = now;
                            queuedForRule = true;
                        }

                        db.Notifications.Add(n);
                        if (n.Status == NotificationStatus.Pending) created++; else suppressed++;
                    }

                    // Starts the cooldown window for the events that follow, including later ones in this batch.
                    if (queuedForRule) { rule.LastNotifiedAt = now; rule.LastNotifiedSeverity = ev.Severity; }
                }
            }
        }

        source.Cursor = newCursor ?? source.Cursor;

        await db.SaveChangesAsync(ct);
        log.LogInformation("Ingested {Source}: {New} new, {Dup} duplicate, {Created} notifications, {Suppressed} suppressed",
            source.Key, fresh.Count, duplicates, created, suppressed);
        return new IngestResult(fresh.Count, duplicates, created, suppressed);
    }

    public static string DedupeKey(Source source, NormalizedEvent item) => $"{source.Key}:{item.ExternalId}";

    private static Event ToEntity(Source source, string key, NormalizedEvent item, DateTime now) => new()
    {
        SourceId = source.Id,
        SourceKey = source.Key,
        DedupeKey = key,
        Category = item.Category,
        Severity = Math.Clamp(item.Severity, 1, 5),
        Title = item.Title,
        Summary = item.Summary,
        Url = item.Url,
        Location = item.Location,
        AttributesJson = item.Attributes.ToJson(),
        OccurredAt = item.OccurredAt,
        IngestedAt = now,
        IsSimulated = item.IsSimulated,
    };
}
