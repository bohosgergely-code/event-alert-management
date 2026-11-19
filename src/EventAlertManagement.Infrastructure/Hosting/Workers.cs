using EventAlertManagement.Infrastructure.Dispatch;
using EventAlertManagement.Infrastructure.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventAlertManagement.Infrastructure.Hosting;

public sealed class SourcePollerWorker(IServiceScopeFactory scopes, ILogger<SourcePollerWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await WaitAsync(timer, ct))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<SourcePollService>().PollDueAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { log.LogError(ex, "Source polling cycle failed"); }
        }
    }

    internal static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}

public sealed class DispatcherWorker(IServiceScopeFactory scopes, ILogger<DispatcherWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await SourcePollerWorker.WaitAsync(timer, ct))
        {
            try
            {
                // Drain everything that is due before sleeping again.
                int handled;
                do
                {
                    using var scope = scopes.CreateScope();
                    handled = await scope.ServiceProvider.GetRequiredService<NotificationDispatcher>().RunOnceAsync(20, ct);
                } while (handled > 0 && !ct.IsCancellationRequested);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { log.LogError(ex, "Dispatch cycle failed"); }
        }
    }
}
