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
    }
}
