using Microsoft.Extensions.Logging;

namespace Obsidian;

public sealed class ScoreboardManager : IScoreboardManager
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
            this.logger.LogWarning("Scoreboard with the name: {name} already exists. This might cause some issues and override already existing scoreboards displaying on clients.", name);

        return new Scoreboard(name, this.server.DefaultWorld.PacketBroadcaster, this.server);
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
