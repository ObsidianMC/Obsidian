using Microsoft.Extensions.Logging;
using Obsidian.API.Logging;

namespace Obsidian;

public sealed class ScoreboardManager : IScoreboardManager
{
    private readonly Server server;

    internal readonly HashSet<string> scoreboards = new HashSet<string>();

    public IScoreboard DefaultScoreboard { get; }

    public ScoreboardManager(Server server)
    {
        this.server = server;

        this.DefaultScoreboard = this.CreateScoreboard("default");
    }

    public IScoreboard CreateScoreboard(string name)
    {
        var loggerProvider = new LoggerProvider(LogLevel.Warning);
        var logger = loggerProvider.CreateLogger("ScoreboardManager");
        if (!this.scoreboards.Add(name))
            logger.LogWarning("Scoreboard with the name: {name} already exists. This might cause some issues and override already existing scoreboards displaying on clients.", name);

        return new Scoreboard(name, this.server);
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
