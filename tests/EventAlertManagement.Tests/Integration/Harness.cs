using EventAlertManagement.Core.Contracts;
using EventAlertManagement.Core.Domain;
using EventAlertManagement.Core.Filters;
using EventAlertManagement.Core.Matching;
using EventAlertManagement.Core.Services;
using EventAlertManagement.Infrastructure.Channels;
using EventAlertManagement.Infrastructure.Dispatch;
using EventAlertManagement.Infrastructure.Persistence;
using EventAlertManagement.Infrastructure.Pipeline;
using EventAlertManagement.Infrastructure.Security;
using EventAlertManagement.Infrastructure.Sources;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EventAlertManagement.Tests.Integration;

public sealed class FakeClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
    public void Advance(TimeSpan by) => UtcNow += by;
}

/// <summary>A channel whose results are scripted by the test.</summary>
public sealed class FakeChannel(string type = "Fake") : INotificationChannel
{
    private readonly Queue<Func<ChannelSendResult>> _script = new();
    public List<NotificationMessage> Sent { get; } = new();
    public string Type => type;

    public FakeChannel Then(ChannelSendResult result) { _script.Enqueue(() => result); return this; }
    public FakeChannel ThenThrow(Exception ex) { _script.Enqueue(() => throw ex); return this; }

    public IReadOnlyList<string> ValidateSettings(string settingsJson) => Array.Empty<string>();
    public string MaskSettings(string settingsJson) => "***";

    public Task<ChannelSendResult> SendAsync(NotificationMessage message, string settingsJson, CancellationToken ct)
    {
        lock (Sent) Sent.Add(message);
        var next = _script.Count > 0 ? _script.Dequeue() : () => ChannelSendResult.Ok();
        return Task.FromResult(next());
    }
}

/// <summary>A real SQLite database file per test class instance, so concurrent contexts behave like production.</summary>
public sealed class Harness : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"eam-test-{Guid.NewGuid():N}.db");
    private readonly DbContextOptions<AppDbContext> _options;
    public FakeClock Clock { get; } = new();
    public FakeChannel Channel { get; } = new();
    public SimulatorSource Simulator { get; } = new();
    public RetryOptions Retry { get; } = new()
    {
        MaxAttempts = 3,
        Delays = new() { TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5) },
        Lease = TimeSpan.FromMinutes(2),
    };
    public ISettingsProtector Protector { get; } = new DataProtectionSettingsProtector(new EphemeralDataProtectionProvider());

    public Harness()
    {
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var db = NewDb();
        DbInitializer.InitializeAsync(db).GetAwaiter().GetResult();
    }

    public AppDbContext NewDb() => new(_options);

    public EventIngestionService Ingestion(AppDbContext db) =>
        new(db, new EventMatcher(), Clock, NullLogger<EventIngestionService>.Instance);

    public NotificationDispatcher Dispatcher(AppDbContext db, IChannelRegistry? registry = null) =>
        new(db, registry ?? new ChannelRegistry(new[] { Channel }), Protector, Clock,
            Options.Create(Retry), NullLogger<NotificationDispatcher>.Instance);

    public SourcePollService Poller(AppDbContext db, params IEventSource[] adapters) =>
        new(db, adapters, Ingestion(db), Clock, NullLogger<SourcePollService>.Instance);

    public Source SimSource(AppDbContext db) => db.Sources.Single(s => s.Key == SimulatorSource.SourceKey);

    // ---- builders ----

    public User AddUser(AppDbContext db, string email = "user@example.com")
    {
        var u = new User { Email = email, DisplayName = email, CreatedAt = Clock.UtcNow };
        db.Users.Add(u); db.SaveChanges();
        return u;
    }

    public ChannelConfig AddChannel(AppDbContext db, User user, string type = "Fake", bool enabled = true, string settings = "{\"secret\":\"super-secret-value\"}")
    {
        var c = new ChannelConfig
        {
            UserId = user.Id, Type = type, Name = type, IsEnabled = enabled,
            SettingsProtected = Protector.Protect(settings),
        };
        db.ChannelConfigs.Add(c); db.SaveChanges();
        return c;
    }

    public AlertRule AddRule(AppDbContext db, User user, EventCategory category, RuleFilters? filters = null,
        int minSeverity = 1, int cooldownSeconds = 0, params ChannelConfig[] channels)
    {
        var r = new AlertRule
        {
            UserId = user.Id, Name = "My rule", Category = category, MinSeverity = minSeverity,
            CooldownSeconds = cooldownSeconds,
            FiltersJson = RuleFilterSerializer.Serialize(filters ?? new NewsFilters()),
            Channels = channels.ToList(),
        };
        db.AlertRules.Add(r); db.SaveChanges();
        return r;
    }

    public static NormalizedEvent NewsItem(string id, string title = "Big storm hits coast", int severity = 3, DateTime? at = null) =>
        new(id, EventCategory.News, severity, title, "Summary text", "https://example.com/" + id, "Somewhere",
            new EventAttributes(), at ?? new DateTime(2026, 6, 1, 11, 0, 0, DateTimeKind.Utc));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _dbPath, _dbPath + "-shm", _dbPath + "-wal" })
            try { File.Delete(f); } catch { /* best effort */ }
    }
}
