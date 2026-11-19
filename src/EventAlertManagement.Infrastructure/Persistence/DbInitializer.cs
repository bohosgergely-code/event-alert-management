using EventAlertManagement.Core.Domain;
using EventAlertManagement.Infrastructure.Sources;
using Microsoft.EntityFrameworkCore;

namespace EventAlertManagement.Infrastructure.Persistence;

public static class DbInitializer
{
    /// <summary>Creates the schema if needed and makes sure the built-in sources exist. EnsureCreated is used instead of migrations for simplicity.</summary>
    public static async Task InitializeAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.EnsureCreatedAsync(ct);

        // The poller, the dispatcher and web requests all write to this one file. Write-ahead logging lets readers
        // and a writer work at the same time instead of blocking each other. The setting is stored in the file.
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);

        if (!await db.Sources.AnyAsync(s => s.Key == SimulatorSource.SourceKey, ct))
        {
            db.Sources.Add(new Source
            {
                Key = SimulatorSource.SourceKey,
                Name = "Simulator",
                Category = null,
                PollIntervalSeconds = 5,
            });
            await db.SaveChangesAsync(ct);
        }

        if (!await db.Sources.AnyAsync(s => s.Key == UsgsEarthquakeSource.SourceKey, ct))
        {
            db.Sources.Add(new Source
            {
                Key = UsgsEarthquakeSource.SourceKey,
                Name = "USGS earthquakes",
                Category = EventCategory.Disasters,
                PollIntervalSeconds = 300,
            });
            await db.SaveChangesAsync(ct);
        }
    }
}
