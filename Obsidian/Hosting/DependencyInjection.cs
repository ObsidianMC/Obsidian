using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Obsidian.API.Configuration;
using Obsidian.Commands.Framework;
using Obsidian.Console;
using Obsidian.Integrated;
using Obsidian.Services;
using Obsidian.WorldData;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using System.IO;

namespace Obsidian.Hosting;
public static class DependencyInjection
{
    /// <summary>
    /// Adds Obsidian's configuration: <c>config/server.json</c> and <c>config/whitelist.json</c> under <c>Paths:Root</c>
    /// when they exist, then environment variables, with command-line arguments overriding them all.
    /// </summary>
    /// <remarks>
    /// The server's paths (see <see cref="ServerConstants.ConfigurePaths"/>) are resolved here, from the configuration the
    /// builder already has (command line and environment), since the configuration files are under them. An integrated
    /// server (<c>Integrated:Enabled</c>) also gets its own defaults, which any configuration overrides.
    /// </remarks>
    public static IHostApplicationBuilder ConfigureObsidian(this IHostApplicationBuilder builder)
    {
        ServerConstants.ConfigurePaths(builder.Configuration);

        builder.Configuration.AddJsonFile(Path.Combine(ServerConstants.ConfigPath, "server.json"), optional: true, reloadOnChange: true);
        builder.Configuration.AddJsonFile(Path.Combine(ServerConstants.ConfigPath, "whitelist.json"), optional: true, reloadOnChange: true);
        builder.Configuration.AddEnvironmentVariables();

        // Command-line arguments come last, so they override the files.
        foreach (var commandLine in builder.Configuration.Sources.OfType<CommandLineConfigurationSource>().ToList())
        {
            builder.Configuration.Sources.Remove(commandLine);
            builder.Configuration.Sources.Add(commandLine);
        }

        if (builder.Configuration.GetValue<bool>("Integrated:Enabled"))
        {
            // Like vanilla's integrated server: up to 8 players, no connection throttling, and local connections only until
            // the world is opened to LAN, which advertises it itself. The client drives the console, and its going away
            // stops the server.
            builder.Configuration.Sources.Insert(0, new MemoryConfigurationSource
            {
                InitialData = new Dictionary<string, string?>
                {
                    ["MaxPlayers"] = "8",
                    ["BindAddress"] = "127.0.0.1",
                    ["AllowLan"] = "false",
                    ["Network:ConnectionThrottle"] = "0",
                    ["Console:Interactive"] = "false",
                    ["Console:EchoCommands"] = "false",
                    ["Console:StopOnEndOfInput"] = "true"
                }
            });
        }

        return builder;
    }

    /// <summary>
    /// Adds the server, its logging and its services. With <c>Integrated:Enabled</c>, the server runs as a game client's
    /// integrated server instead (see <c>docs/integrated-server.md</c>): standard output carries control events, logs go to
    /// standard error, and standard input also takes control commands.
    /// </summary>
    public static IHostApplicationBuilder AddObsidian(this IHostApplicationBuilder builder)
    {
        var integrated = builder.Configuration.GetValue<bool>("Integrated:Enabled");

        // filename with date,time
        Directory.CreateDirectory(ServerConstants.LogsPath);
        var logFile = Path.Combine(ServerConstants.LogsPath, $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
        var logFileStream = new FileStream(logFile, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);

        // Clear the host's default providers first; clearing later would also remove OpenTelemetry.
        builder.Logging.ClearProviders();

        builder.Logging.AddOpenTelemetry(x =>
        {
            x.IncludeScopes = true;
            x.IncludeFormattedMessage = true;
        });

        // An integrated server's standard output is its control channel, so its log goes to standard error as plain lines.
        //Console logger can be edited through server.config https://learn.microsoft.com/en-us/dotnet/core/extensions/console-log-formatter
        if (integrated)
            builder.Logging.AddProvider(new StreamLoggerProvider(System.Console.OpenStandardError()));
        else
            builder.Logging.AddServerConsoleLogging();

        builder.Logging.AddProvider(new StreamLoggerProvider(logFileStream));

        builder.Services.Configure<ServerConfiguration>(builder.Configuration);
        builder.Services.Configure<WhitelistConfiguration>(builder.Configuration);
        builder.Services.Configure<IntegratedConfiguration>(builder.Configuration.GetSection("Integrated"));
        builder.Services.Configure<NewWorldConfiguration>(builder.Configuration.GetSection("NewWorld"));

        if (integrated)
        {
            builder.Services.AddSingleton<IntegratedSession>();
            builder.Services.AddSingleton(_ => IntegratedEventWriter.ForStandardOutput());
            builder.Services.AddSingleton<IntegratedServerService>();
            builder.Services.AddSingleton<IServerEnvironment, IntegratedServerEnvironment>();

            // Ahead of the other console handlers, which would take control lines for unknown commands.
            builder.Services.AddSingleton<IConsoleCommandHandler>(sp => sp.GetRequiredService<IntegratedServerService>());
            builder.Services.AddHostedService(sp => sp.GetRequiredService<IntegratedServerService>());
        }
        else
        {
            builder.Services.AddSingleton<IServerEnvironment, DefaultServerEnvironment>();
        }
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
                tracing.AddHttpClientInstrumentation();
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
