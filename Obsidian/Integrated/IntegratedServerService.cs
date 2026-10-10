using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Obsidian.Console;
using Obsidian.Services;
using Obsidian.Utilities;
using Obsidian.WorldData;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Obsidian.Integrated;

/// <summary>
/// Runs an integrated server's side of the control channel: reports loading progress, readiness, saves and tick times as
/// events, and carries out the client's commands, which arrive as console lines (see <see cref="IntegratedProtocol"/>).
/// </summary>
public sealed partial class IntegratedServerService(
    IServer server,
    WorldManager worldManager,
    IntegratedSession session,
    IntegratedEventWriter events,
    IHostApplicationLifetime lifetime,
    ILogger<IntegratedServerService> logger) : BackgroundService, IConsoleCommandHandler
{
    // Progress events go out at most this often.
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(250);

    private static readonly TimeSpan StatsInterval = TimeSpan.FromSeconds(1);

    private readonly Server server = (Server)server;

    // Publishing runs once at a time, from the control command or /publish.
    private readonly SemaphoreSlim publishLock = new(1, 1);

    private int stoppingReported;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        this.server.SaveStarted += autosave => events.Write(IntegratedEvent.Saving(autosave));
        this.server.SaveCompleted += (autosave, failure) =>
            events.Write(failure is null ? IntegratedEvent.Saved(autosave) : IntegratedEvent.SaveFailed(failure.Message));
        this.server.PlayerSaveFailed += message => events.Write(IntegratedEvent.PlayerSaveFailed(message));

        // Every graceful stop is announced, whether the client asked for it, its input ended, or the host stops.
        lifetime.ApplicationStopping.Register(this.ReportStopping);

        try
        {
            WorldManager.LoadProgress? reported = null;
            while (!(this.server.Started && worldManager.ReadyToJoin))
            {
                var progress = worldManager.Progress;
                if (progress != reported)
                {
                    events.Write(IntegratedEvent.Progress(progress.Stage, progress.Percent));
                    reported = progress;
                }

                await Task.Delay(ProgressInterval, stoppingToken);
            }

            events.Write(IntegratedEvent.Ready(this.server.Port));

            // Tick times for the client's debug screen, while the worlds run.
            using var timer = new PeriodicTimer(StatsInterval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (!this.server.Paused)
                    events.Write(IntegratedEvent.Stats(this.server.AverageTickMilliseconds));
            }
        }
        catch (OperationCanceledException)
        {
            // The server is stopping.
        }
    }

    public ValueTask<ConsoleCompletion?> CompleteAsync(string commandLine, int cursor, CancellationToken cancellationToken) =>
        ValueTask.FromResult<ConsoleCompletion?>(null);

    /// <summary>
    /// Carries out a control command; other lines are left to the other console command handlers.
    /// </summary>
    public async ValueTask<bool> HandleAsync(string commandLine, CancellationToken cancellationToken)
    {
        if (!IntegratedProtocol.IsControlLine(commandLine))
            return false;

        if (!IntegratedProtocol.TryParseCommand(commandLine, out var command))
        {
            Log.MalformedCommand(logger, commandLine);
            return true;
        }

        // Commands queued behind a stop would race the final save, or open a listener after the others closed.
        if (this.server.Stopping)
        {
            Log.IgnoredWhileStopping(logger, command.Command);
            return true;
        }

        switch (command.Command)
        {
            case "pause":
                await this.PauseAsync();
                break;
            case "resume":
                this.server.Resume();
                events.Write(IntegratedEvent.Resumed());
                break;
            case "save":
                await this.server.SaveEverythingAsync(autosave: false);
                break;
            case "stop":
                lifetime.StopApplication();
                break;
            case "publish":
                if (!TryParseGameMode(command.GameMode, out var gameMode))
                {
                    events.Write(IntegratedEvent.PublishFailed($"Unknown game mode '{command.GameMode}'."));
                    break;
                }

                await this.PublishAsync(command.Port ?? 0, gameMode, command.AllowCommands);
                break;
            default:
                Log.UnknownCommand(logger, command.Command);
                break;
        }

        return true;
    }

    /// <summary>
    /// Opens the world to LAN, like vanilla's <c>IntegratedServer.publishServer</c>: listens on every address at
    /// <paramref name="port"/> besides the client's own connection, advertises the world to the LAN, lets other players
    /// join, forces <paramref name="gameMode"/> on them, and when <paramref name="allowCommands"/> lets everyone use
    /// commands. Reports the outcome as an event.
    /// </summary>
    /// <param name="port">The port, or 0 for a free one.</param>
    /// <param name="gameMode">The game mode for players who join from now on, or <c>null</c> for the world's default.</param>
    /// <returns>The LAN port, or <c>null</c> when publishing failed.</returns>
    public async Task<int?> PublishAsync(int port, GameMode? gameMode, bool allowCommands)
    {
        await this.publishLock.WaitAsync();
        try
        {
            if (this.server.Stopping)
            {
                events.Write(IntegratedEvent.PublishFailed("The server is stopping."));
                return null;
            }

            if (!this.server.Started || !worldManager.ReadyToJoin)
            {
                events.Write(IntegratedEvent.PublishFailed("The world isn't ready yet."));
                return null;
            }

            if (session.IsPublished)
            {
                events.Write(IntegratedEvent.PublishFailed("The world is already open to LAN."));
                return null;
            }

            int boundPort;
            try
            {
                boundPort = await this.server.ListenAsync(new IPEndPoint(IPAddress.Any, port));
            }
            catch (Exception ex) when (ex is SocketException or InvalidOperationException)
            {
                Log.PublishFailed(logger, ex, port);
                events.Write(IntegratedEvent.PublishFailed(ex.Message));
                return null;
            }

            session.Publish(gameMode, allowCommands);
            Log.Published(logger, boundPort);

            // Vanilla's integrated server MOTD, "<player> - <world>".
            var motd = $"{session.Configuration.LocalPlayerName} - {this.server.DefaultWorld.LevelData.LevelName}";
            _ = LanBroadcasterService.BroadcastAsync(() => (motd, boundPort), logger, lifetime.ApplicationStopping);

            foreach (var player in this.server.OnlinePlayers.Values)
                await ((OperatorList)this.server.Operators).SendPermissionLevelAsync(player);

            events.Write(IntegratedEvent.Published(boundPort));
            return boundPort;
        }
        finally
        {
            this.publishLock.Release();
        }
    }

    /// <summary>
    /// Stops ticking the worlds after the tick in progress, and saves everything once on the way, like vanilla's
    /// integrated server when its game pauses.
    /// </summary>
    private async Task PauseAsync()
    {
        if (await this.server.PauseAsync())
        {
            Log.SavingAndPausing(logger);
            await this.server.SaveEverythingAsync(autosave: false);
        }

        events.Write(IntegratedEvent.Paused());
    }

    private void ReportStopping()
    {
        if (Interlocked.Exchange(ref this.stoppingReported, 1) == 0)
            events.Write(IntegratedEvent.Stopping());
    }

    /// <summary>
    /// Parses vanilla's game mode id (<c>survival</c>, <c>creative</c>, <c>adventure</c>, <c>spectator</c>); none is
    /// <c>null</c>.
    /// </summary>
    internal static bool TryParseGameMode(string? id, out GameMode? gameMode)
    {
        gameMode = null;
        if (string.IsNullOrEmpty(id))
            return true;

        if (!Enum.TryParse<GameMode>(id, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            return false;

        gameMode = parsed;
        return true;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Ignoring a malformed control line: {Line}")]
        public static partial void MalformedCommand(ILogger logger, string line);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Ignoring the unknown control command {Command}")]
        public static partial void UnknownCommand(ILogger logger, string command);

        [LoggerMessage(Level = LogLevel.Information, Message = "Ignoring the control command {Command}: the server is stopping")]
        public static partial void IgnoredWhileStopping(ILogger logger, string command);

        [LoggerMessage(Level = LogLevel.Information, Message = "Saving and pausing game...")]
        public static partial void SavingAndPausing(ILogger logger);

        [LoggerMessage(Level = LogLevel.Information, Message = "Started serving on {Port}")]
        public static partial void Published(ILogger logger, int port);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Opening to LAN on port {Port} failed")]
        public static partial void PublishFailed(ILogger logger, Exception exception, int port);
    }
}
