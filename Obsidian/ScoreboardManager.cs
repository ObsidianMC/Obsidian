using Microsoft.Extensions.Logging;
using Obsidian.Plugins;

namespace Obsidian;

public sealed partial class ScoreboardManager : IScoreboardManager
{
    private readonly IServer server;
    private readonly ILogger logger;

    private readonly HashSet<string> scoreboards = [];

    public IScoreboard DefaultScoreboard { get; }

    public ScoreboardManager(IServer server, ILoggerFactory loggerFactory)
    {
        this.server = server;
        this.logger = loggerFactory.CreateLogger<ScoreboardManager>();

        this.DefaultScoreboard = this.CreateScoreboard("default");
    }

    /// <remarks>
    /// Scoreboards created through a plugin's <see cref="IServer"/> are the plugin's: unloading it retires them (see
    /// <see cref="Scoreboard.Retire"/>) and frees their names.
    /// </remarks>
    public IScoreboard CreateScoreboard(string name) => this.CreateScoreboard(name, owner: null);

    /// <summary>
    /// Creates a scoreboard for <paramref name="owner"/>, whose unloading retires it, or for the server.
    /// </summary>
    /// <exception cref="InvalidOperationException">The owner is unloading; the scoreboard was retired.</exception>
    internal Scoreboard CreateScoreboard(string name, PluginContainer? owner)
    {
        bool added;
        lock (this.scoreboards)
            added = this.scoreboards.Add(name);

        if (!added)
            Log.DuplicateScoreboard(this.logger, name);

        var scoreboard = new Scoreboard(name, this.server.DefaultWorld.PacketBroadcaster);

        owner?.TrackAddition(() => this.Remove(scoreboard, name, added));

        return scoreboard;
    }

    // Retires a plugin's scoreboard, and frees its name unless it was taken already.
    private void Remove(Scoreboard scoreboard, string name, bool ownsName)
    {
        scoreboard.Retire();

        if (!ownsName)
            return;

        lock (this.scoreboards)
            this.scoreboards.Remove(name);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Scoreboard {Name} already exists; clients may show the new one in its place")]
        public static partial void DuplicateScoreboard(ILogger logger, string name);
    }
}

/// <summary>
/// Criterias are used with the default scoreboard
/// </summary>
public enum ScoreboardCriteria
{
    Dummy,
    Trigger,
    DeathCount,
    PlayerKillCount,
    TotalKillCount,
    Health,
    Food,
    Air,
    Armor,
    Xp,
    Level,

    TeamKill,
    KilledByTeam
}
