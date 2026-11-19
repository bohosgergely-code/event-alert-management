using System.Diagnostics;
using EventAlertManagement.Core.Contracts;
using EventAlertManagement.Core.Domain;
using EventAlertManagement.Core.Services;
using EventAlertManagement.Infrastructure.Persistence;
using EventAlertManagement.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EventAlertManagement.Infrastructure.Dispatch;

/// <summary>
/// Delivers due notifications. Claims work with an atomic update and a lease, classifies failures as
/// transient or permanent, and retries only transient ones (ADR-003, ADR-013).
/// </summary>
public sealed class NotificationDispatcher(
    AppDbContext db, IChannelRegistry registry, ISettingsProtector protector, IClock clock,
    IOptions<RetryOptions> retryOptions, ILogger<NotificationDispatcher> log)
{
    private readonly RetryOptions _retry = retryOptions.Value;

    /// <summary>Processes up to <paramref name="batchSize"/> due notifications. Returns how many were handled.</summary>
    public async Task<int> RunOnceAsync(int batchSize, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var ids = await db.Notifications
            .Where(n => (n.Status == NotificationStatus.Pending && (n.NextAttemptAt == null || n.NextAttemptAt <= now))
                     || (n.Status == NotificationStatus.Retrying && n.NextAttemptAt <= now)
                     || (n.Status == NotificationStatus.Sending && n.LeaseUntil < now))
            .OrderBy(n => n.CreatedAt)
            .Select(n => n.Id)
            .Take(batchSize)
            .ToListAsync(ct);

        var handled = 0;
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            if (!await TryClaimAsync(id, ct)) continue; // Someone else took it.
            try { await DeliverAsync(id, ct); }
            catch (DbUpdateException ex)
            {
                // The rule or channel was deleted while this was being sent, so there is nothing left to record on.
                // The rest of the batch must not wait for the next cycle because of that.
                log.LogWarning(ex, "Notification {Id} disappeared while it was being delivered", id);
            }
            handled++;
        }
        return handled;
    }

    private async Task<bool> TryClaimAsync(Guid id, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var leaseUntil = now + _retry.Lease;
        var rows = await db.Notifications
            .Where(n => n.Id == id &&
                ((n.Status == NotificationStatus.Pending && (n.NextAttemptAt == null || n.NextAttemptAt <= now))
              || (n.Status == NotificationStatus.Retrying && n.NextAttemptAt <= now)
              || (n.Status == NotificationStatus.Sending && n.LeaseUntil < now)))
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.Status, NotificationStatus.Sending)
                .SetProperty(n => n.LeaseUntil, leaseUntil), ct);
        return rows == 1;
    }

    private async Task DeliverAsync(Guid id, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var n = await db.Notifications
            .Include(x => x.Event).Include(x => x.ChannelConfig).Include(x => x.Rule)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (n is null) return; // deleted after it was claimed

        // Decide with the state of the rule and channel NOW, not the state when the alert was queued.
        var suppression = await SuppressionReasonAsync(n, ct);
        if (suppression is not null)
        {
            n.Status = NotificationStatus.Suppressed;
            n.SuppressedReason = suppression;
            n.LeaseUntil = null;
            n.NextAttemptAt = null;
            await db.SaveChangesAsync(ct);
            return;
        }

        // Attempt numbers keep counting across manual retries, so history never shows the same number twice.
        var attemptNumber = (await db.DeliveryAttempts.Where(a => a.NotificationId == id).MaxAsync(a => (int?)a.AttemptNumber, ct) ?? 0) + 1;
        var startedAt = clock.UtcNow;
        var sw = Stopwatch.StartNew();
        ChannelSendResult result;

        try
        {
            result = await SendAsync(n, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Shutting down. The lease expires and another cycle picks it up.
        }
        catch (Exception ex)
        {
            // Only the type name is stored: exception messages from HTTP and SMTP clients can contain URLs.
            log.LogWarning(ex, "Unexpected error delivering notification {Id}", id);
            result = ChannelSendResult.Transient($"Unexpected error: {ex.GetType().Name}");
        }
        sw.Stop();

        var now = clock.UtcNow;
        n.AttemptCount += 1; // attempts in the current round; this is what the retry limit counts
        n.LeaseUntil = null;
        // Added explicitly: a client-generated GUID key on an entity reached through a tracked collection
        // is otherwise treated as existing (Modified) and the save fails with a concurrency error.
        db.DeliveryAttempts.Add(new DeliveryAttempt
        {
            NotificationId = n.Id,
            AttemptNumber = attemptNumber,
            StartedAt = startedAt,
            DurationMs = (int)sw.ElapsedMilliseconds,
            Outcome = result.Outcome switch
            {
                ChannelSendOutcome.Success => AttemptOutcome.Success,
                ChannelSendOutcome.TransientFailure => AttemptOutcome.TransientFailure,
                _ => AttemptOutcome.PermanentFailure,
            },
            ErrorMessage = result.Outcome == ChannelSendOutcome.Success ? null : ErrorSanitizer.Sanitize(result.Error),
        });

        switch (result.Outcome)
        {
            case ChannelSendOutcome.Success:
                n.Status = NotificationStatus.Sent;
                n.SentAt = now;
                n.NextAttemptAt = null;
                break;
            case ChannelSendOutcome.TransientFailure:
                var next = RetryPolicy.NextAttemptAt(_retry, n.AttemptCount, now);
                n.Status = next is null ? NotificationStatus.Failed : NotificationStatus.Retrying;
                n.NextAttemptAt = next;
                break;
            default:
                n.Status = NotificationStatus.Failed;
                n.NextAttemptAt = null;
                break;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Why this alert should not be sent after all, or null. These are choices someone made, not delivery failures.</summary>
    private async Task<string?> SuppressionReasonAsync(Notification n, CancellationToken ct)
    {
        if (n.Rule is { IsEnabled: false }) return "The rule was switched off before this alert was delivered.";
        var config = n.ChannelConfig!;
        if (!config.IsEnabled) return "Channel is disabled.";

        // Compared in memory and ignoring case, exactly like the channel registry and ingestion do (the table is tiny).
        var switchedOff = await db.ChannelTypeSettings.Where(t => !t.IsEnabled).Select(t => t.Type).ToListAsync(ct);
        return switchedOff.Contains(config.Type, StringComparer.OrdinalIgnoreCase)
            ? $"{config.Type} notifications are switched off by an administrator."
            : null;
    }

    private async Task<ChannelSendResult> SendAsync(Notification n, CancellationToken ct)
    {
        var config = n.ChannelConfig!;

        var channel = registry.Find(config.Type);
        if (channel is null) return ChannelSendResult.Permanent($"Unknown channel type '{config.Type}'.");

        string settings;
        try { settings = protector.Unprotect(config.SettingsProtected); }
        catch (Exception) { return ChannelSendResult.Permanent("Channel settings could not be read."); }

        var problems = channel.ValidateSettings(settings);
        if (problems.Count > 0) return ChannelSendResult.Permanent("Channel settings are invalid: " + string.Join(" ", problems));

        return await channel.SendAsync(new NotificationMessage(n.Event!, n.Rule!.Name), settings, ct);
    }
}
