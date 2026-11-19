using EventAlertManagement.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace EventAlertManagement.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<ChannelConfig> ChannelConfigs => Set<ChannelConfig>();
    public DbSet<AlertRule> AlertRules => Set<AlertRule>();
    public DbSet<Source> Sources => Set<Source>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<DeliveryAttempt> DeliveryAttempts => Set<DeliveryAttempt>();
    public DbSet<ChannelTypeSetting> ChannelTypeSettings => Set<ChannelTypeSetting>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Everything is UTC. SQLite drops the Kind, so restore it on read.
        builder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        builder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(320);
        });

        b.Entity<ChannelConfig>(e =>
        {
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Type).HasMaxLength(50);
        });

        b.Entity<AlertRule>(e =>
        {
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Channels).WithMany();
            e.HasIndex(x => new { x.Category, x.IsEnabled });
        });

        b.Entity<Source>(e => e.HasIndex(x => x.Key).IsUnique());
        b.Entity<ChannelTypeSetting>(e => { e.HasKey(x => x.Type); e.Property(x => x.Type).HasMaxLength(50); });

        b.Entity<Event>(e =>
        {
            e.HasIndex(x => x.DedupeKey).IsUnique();
            e.HasIndex(x => x.OccurredAt);
            e.HasOne(x => x.Source).WithMany().HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Notification>(e =>
        {
            // The guarantee behind "the same event never notifies the same destination twice for a rule".
            e.HasIndex(x => new { x.RuleId, x.EventId, x.ChannelConfigId }).IsUnique();
            e.HasIndex(x => new { x.Status, x.NextAttemptAt });
            e.HasOne(x => x.Rule).WithMany().HasForeignKey(x => x.RuleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Event).WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ChannelConfig).WithMany().HasForeignKey(x => x.ChannelConfigId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Attempts).WithOne(a => a.Notification).HasForeignKey(a => a.NotificationId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
