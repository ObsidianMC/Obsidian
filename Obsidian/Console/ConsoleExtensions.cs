using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using System.IO;

namespace Obsidian.Console;

/// <summary>Registers standard terminal output and independent command input.</summary>
public static class ConsoleExtensions
{
    public static ILoggingBuilder AddServerConsoleLogging(this ILoggingBuilder builder)
    {
        builder.Services.AddOptions<SimpleConsoleFormatterOptions>()
            .Configure(options =>
            {
                options.ColorBehavior = LoggerColorBehavior.Enabled;
                options.TimestampFormat = "HH:mm:ss ";
                options.SingleLine = true;
                options.IncludeScopes = true;
            });

        builder.AddSimpleConsole();

        var standardProvider = builder.Services.FirstOrDefault(service =>
            service.ServiceType == typeof(ILoggerProvider)
            && service.ImplementationType == typeof(ConsoleLoggerProvider));

        if (standardProvider is not null)
            builder.Services.Remove(standardProvider);

        AddTerminal(builder.Services);
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ILoggerProvider, CoordinatedConsoleLoggerProvider>());

        return builder;
    }

    public static IServiceCollection AddConsoleCommands(this IServiceCollection services)
    {
        AddTerminal(services);

        services.TryAddSingleton(System.Console.In);
        services.AddHostedService<ConsoleCommandService>();

        return services;
    }

    private static void AddTerminal(IServiceCollection services)
    {
        services.AddOptions<ConsoleCommandOptions>().BindConfiguration("Console");
        services.TryAddSingleton<IConsoleDevice, ConsoleDevice>();
        services.TryAddSingleton<ConsoleTerminal>();
    }
}
