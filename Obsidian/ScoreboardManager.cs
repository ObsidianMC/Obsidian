using Microsoft.Extensions.Logging;

namespace Obsidian;

public sealed class ScoreboardManager : IScoreboardManager
{
    private readonly Server server;

    internal readonly HashSet<string> scoreboards = new HashSet<string>();

    public IScoreboard DefaultScoreboard { get; }

    public ScoreboardManager(Server server)
    {
        this.server = server;

        DefaultScoreboard = CreateScoreboard("default");
    }

    public IScoreboard CreateScoreboard(string name)
    {
        if (!scoreboards.Add(name))
            server.Logger.LogWarning($"Scoreboard with the name: {name} already exists. This might cause some issues and override already existing scoreboards displaying on clients.");

        return new Scoreboard(name, server);
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
