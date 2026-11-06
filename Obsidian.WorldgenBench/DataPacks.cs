using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.Commands.Framework;
using Obsidian.Plugins;
using Obsidian.Services;
using System;
using System.Reflection;

namespace WorldgenBench;

/// <summary>
/// Applies a data packs folder the way the server's startup does, with no plugins and before any world loads, so the
/// bench generates with the packs' noise, biomes, features and structures.
/// </summary>
internal static class DataPacks
{
    public static void Load(string folder)
    {
        var server = NoServer.Create();
        var services = new ServiceCollection();
        services.AddSingleton(server);
        var provider = services.BuildServiceProvider();

        var manager = new PluginManager(
            server,
            provider,
            new EventDispatcher(NullLogger<EventDispatcher>.Instance, provider),
            new CommandHandler(provider, NullLogger<CommandHandler>.Instance),
            new ConsoleLogger<PluginManager>(),
            new ConfigurationBuilder().Build());

        manager.FinishStartup(folder);
    }

    // Startup only asks the server for its worlds, of which there are none yet; anything else it asks is a bench bug.
    public class NoServer : DispatchProxy
    {
        public static IServer Create() => DispatchProxy.Create<IServer, NoServer>();

        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            "get_WorldManager" => NoWorlds.Create(),
            _ => throw new NotSupportedException($"The bench has no server: {method?.Name}.")
        };
    }

    public class NoWorlds : DispatchProxy
    {
        public static IWorldManager Create() => DispatchProxy.Create<IWorldManager, NoWorlds>();

        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            nameof(IWorldManager.GetAvailableWorlds) => Array.Empty<IWorld>(),
            _ => throw new NotSupportedException($"The bench has no worlds: {method?.Name}.")
        };
    }

    // Warnings and errors only, so a pack's unread or failing files show.
    private sealed class ConsoleLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (this.IsEnabled(logLevel))
                Console.Error.WriteLine($"[{logLevel}] {formatter(state, exception)} {exception?.Message}");
        }
    }
}
