using EventAlertManagement.Core.Contracts;
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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EventAlertManagement.Infrastructure.Hosting;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEventAlertInfrastructure(
        this IServiceCollection services, IConfiguration config, bool runBackgroundWorkers = true)
    {
        services.AddDbContext<AppDbContext>(o =>
            o.UseSqlite(config.GetConnectionString("Default") ?? "Data Source=eventalert.db"));

        // Keys must survive restarts or stored channel settings become unreadable (ADR-014).
        var dp = services.AddDataProtection().SetApplicationName("EventAlertManagement");
        var keysPath = config["DataProtection:KeysPath"];
        if (!string.IsNullOrWhiteSpace(keysPath)) dp.PersistKeysToFileSystem(new DirectoryInfo(keysPath));

        services.AddSingleton<ISettingsProtector, DataProtectionSettingsProtector>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IEventMatcher, EventMatcher>();

        services.Configure<RetryOptions>(config.GetSection("Retry"));
        services.Configure<SmtpOptions>(config.GetSection("Smtp"));

        // Channels: adding one means one class and one line here.
        services.AddSingleton<IEmailTransport, MailKitEmailTransport>();
        services.AddHttpClient(SlackChannel.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddSingleton<INotificationChannel, EmailChannel>();
        services.AddSingleton<INotificationChannel, SlackChannel>();
        services.AddWebhookChannel(config); // third channel: one line here, everything else lives in WebhookChannel.cs
        services.AddSingleton<IChannelRegistry, ChannelRegistry>();

        // Sources
        services.AddSingleton<SimulatorSource>();
        services.AddSingleton<IEventSource>(sp => sp.GetRequiredService<SimulatorSource>());

        services.Configure<UsgsOptions>(config.GetSection("Sources:Usgs"));
        services.AddHttpClient(UsgsEarthquakeSource.HttpClientName)
            .ConfigureHttpClient(c =>
            {
                c.Timeout = TimeSpan.FromSeconds(20);
                c.MaxResponseContentBufferSize = 10 * 1024 * 1024;
            });
        services.AddSingleton<IEventSource, UsgsEarthquakeSource>();

        services.AddScoped<EventIngestionService>();
        services.AddScoped<SourcePollService>();
        services.AddScoped<NotificationDispatcher>();
        services.AddScoped<NotificationRetryService>();

        if (runBackgroundWorkers)
        {
            services.AddHostedService<SourcePollerWorker>();
            services.AddHostedService<DispatcherWorker>();
        }
        return services;
    }
}
