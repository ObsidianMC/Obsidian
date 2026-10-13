using Microsoft.Extensions.Logging;
using Obsidian.Plugins;
using System.Reflection;
using System.Runtime.CompilerServices;

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
    /// A plugin's scoreboards are removed when it unloads: their objective and teams are removed from the players
    /// they're shown to, and their names are free again.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public IScoreboard CreateScoreboard(string name)
    {
        bool added;
        lock (this.scoreboards)
            added = this.scoreboards.Add(name);

        if (!added)
            Log.DuplicateScoreboard(this.logger, name);

        var scoreboard = new Scoreboard(name, this.server.DefaultWorld.PacketBroadcaster);

        PluginContainer.TrackAddition(Assembly.GetCallingAssembly(), () => this.Remove(scoreboard, name, added));

        return scoreboard;
    }

    // Removes a plugin's scoreboard from its players, and frees its name unless it was taken already.
    private void Remove(Scoreboard scoreboard, string name, bool ownsName)
    {
        if (scoreboard.Objective is not null)
            scoreboard.RemoveObjective();

        foreach (var team in scoreboard.Teams.ToArray())
            team.Delete();

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
