using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Obsidian.API.Configuration;
using Obsidian.Commands.Framework;
using Obsidian.Console;
using Obsidian.Services;
using Obsidian.WorldData;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using System.IO;
using System.Net.Http;

namespace Obsidian.Hosting;
public static class DependencyInjection
{
    public static IHostApplicationBuilder ConfigureObsidian(this IHostApplicationBuilder builder)
    {
        builder.Configuration.AddJsonFile(Path.Combine("config", "server.json"), optional: false, reloadOnChange: true);
        builder.Configuration.AddJsonFile(Path.Combine("config", "whitelist.json"), optional: false, reloadOnChange: true);
        builder.Configuration.AddEnvironmentVariables();

        return builder;
    }

    public static IHostApplicationBuilder AddObsidian(this IHostApplicationBuilder builder)
    {
        // filename with date,time
        var logFile = $"logs/{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log";
        var logFileStream = new FileStream(logFile, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);

        // Clear the host's default providers first; clearing later would also remove OpenTelemetry.
        builder.Logging.ClearProviders();

        builder.Logging.AddOpenTelemetry(x =>
        {
            x.IncludeScopes = true;
            x.IncludeFormattedMessage = true;
        });

        //Console logger can be edited through server.config https://learn.microsoft.com/en-us/dotnet/core/extensions/console-log-formatter
        builder.Logging.AddServerConsoleLogging();

        builder.Logging.AddProvider(new StreamLoggerProvider(logFileStream));

        builder.Services.Configure<ServerConfiguration>(builder.Configuration);
        builder.Services.Configure<WhitelistConfiguration>(builder.Configuration);

        builder.Services.AddSingleton<IServerEnvironment, DefaultServerEnvironment>();
        builder.Services.AddSingleton<CommandHandler>();
        builder.Services.AddSingleton<WorldManager>();
        builder.Services.AddSingleton<PacketBroadcaster>();
        builder.Services.AddSingleton<IServer, Server>();
        builder.Services.AddSingleton<IUserCache, UserCache>();
        builder.Services.AddSingleton<EventDispatcher>();
        builder.Services.AddSingleton<ILevelFactory, LevelFactory>();

        builder.Services.AddSingleton<IConsoleCommandHandler, RegistryConsoleCommandHandler>();
        builder.Services.AddConsoleCommands();

        builder.Services.AddHttpClient();
        builder.Services.AddHttpClient("Feedback")
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            .RemoveAllLoggers();

        builder.Services.AddHostedService(sp => sp.GetRequiredService<PacketBroadcaster>());
        builder.Services.AddHostedService<ObsidianHostingService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<WorldManager>());
        builder.Services.AddHostedService<LanBroadcasterService>();

        builder.Services.AddSingleton<IEventDispatcher>(x => x.GetRequiredService<EventDispatcher>());
        builder.Services.AddSingleton<IWorldManager>(sp => sp.GetRequiredService<WorldManager>());
        builder.Services.AddSingleton<IPacketBroadcaster>(sp => sp.GetRequiredService<PacketBroadcaster>());

        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                if(builder.Environment.IsDevelopment())
                {
                    tracing.SetSampler<AlwaysOnSampler>();
                }

                //tracing.AddConsoleExporter();
                tracing.AddHttpClientInstrumentation(options =>
                    options.FilterHttpRequestMessage = request => !request.Options.TryGetValue(Server.FeedbackRequestOption, out var feedback) || !feedback);
            })
            .WithMetrics(metrics =>
            {
                //metrics.AddConsoleExporter();

                metrics.AddRuntimeInstrumentation().AddMeter("Obsidian.Server", "Obsidian.Client", "System.Net.Http");
            });

        builder.AddOpenTelemetryExporters();

        builder.Services.AddSingleton<ServerMetrics>();

        return builder;
    }

    private static IHostApplicationBuilder AddOpenTelemetryExporters(this IHostApplicationBuilder builder)
    {
        var useOtlpExporter = !builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"].IsNullOrWhitespace();

        if(useOtlpExporter)
        {
            builder.Services.Configure<OpenTelemetryLoggerOptions>(logging => logging.AddOtlpExporter());
            builder.Services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddOtlpExporter());
            builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddOtlpExporter());
        }

        return builder;
    }

}
