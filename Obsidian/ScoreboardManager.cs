using Microsoft.Extensions.Logging;

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

    public IScoreboard CreateScoreboard(string name)
    {
        if (!this.scoreboards.Add(name))
            Log.DuplicateScoreboard(this.logger, name);

        return new Scoreboard(name, this.server.DefaultWorld.PacketBroadcaster, this.server);
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
