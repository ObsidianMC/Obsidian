using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Scoreboard;
using System.Threading;

namespace Obsidian;

/// <remarks>
/// A plugin's scoreboard is retired when the plugin unloads (see <see cref="Retire"/>): its objective and teams are
/// removed from players' screens, and changing it, or its teams, or showing it again throws
/// <see cref="InvalidOperationException"/>, so a handler still running can't bring it back.
/// </remarks>
public class Scoreboard(string name, IPacketBroadcaster packetBroadcaster) : IScoreboard
{
    private readonly IPacketBroadcaster packetBroadcaster = packetBroadcaster;

    internal readonly string name = name;
    internal readonly Dictionary<string, Score> scores = [];

    private bool retired;

    /// <summary>
    /// Taken by every change of the scoreboard and its teams, and by showing it (see <see cref="Display"/>),
    /// which send their packets before letting go, so none is sent after <see cref="Retire"/> removed them.
    /// </summary>
    internal Lock Gate { get; } = new();

    /// <summary>
    /// Whether its plugin unloaded, which refuses changes. Read under <see cref="Gate"/>.
    /// </summary>
    internal bool IsRetired => this.retired;

    /// <summary>
    /// The entity ids of the players shown the scoreboard. Change it with <see cref="AddPlayer"/> and
    /// <see cref="RemovePlayer"/>.
    /// </summary>
    public ConcurrentHashSet<int> Players { get; } = [];

    public ScoreboardObjective Objective { get; private set; }

    public List<ITeam> Teams { get; set; } = [];

    public void CreateOrUpdateObjective(ChatMessage title, DisplayType displayType = DisplayType.Integer)
    {
        lock (this.Gate)
        {
            this.ThrowIfRetired();

            var packet = new SetObjectivePacket
            {
                ObjectiveName = this.name,
                Mode = this.Objective is not null ? ScoreboardMode.Update : ScoreboardMode.Create,
                Value = title,
                Type = displayType
            };

            if (this.Objective is not null)
            {
                this.UpdateObjective(packet);
                return;
            }

            this.Objective = new ScoreboardObjective
            (
                this.name,
                title,
                displayType
            );

            var players = this.Players.ToArray();

            foreach (var score in this.scores.Select(x => x.Value).OrderByDescending(x => x.Value))
            {

                this.packetBroadcaster.QueuePacketTo(new SetScorePacket
                {
                    EntityName = score.DisplayText,
                    ObjectiveName = this.name,
                    Value = score.Value
                }, players);
            }
        }
    }

    public void CreateOrUpdateScore(string scoreName, string displayText, int? value = null)
    {
        lock (this.Gate)
        {
            this.ThrowIfRetired();

            var score = new Score(displayText, value ?? 0);

            var players = this.Players.ToArray();

            if (this.scores.TryGetValue(scoreName, out var cachedScore))
            {
                score = cachedScore;

                if (value.HasValue)
                    score.Value = (int)value;

                this.packetBroadcaster.QueuePacketTo(new SetScorePacket
                {
                    EntityName = score.DisplayText,
                    ObjectiveName = this.name,
                    Value = score.Value
                }, players);

                score.DisplayText = displayText;
            }
            else
            {
                if (this.scores.Count > 0)
                {
                    score.Value = this.scores.Select(x => x.Value).OrderByDescending(x => x.Value).Last().Value;

                    foreach (var element in this.scores.Select(x => x.Value).OrderByDescending(x => x.Value))
                        element.Value += 1;
                }

                this.scores[scoreName] = score;
            }

            foreach (var (_, s) in this.scores.OrderBy(x => x.Value.Value))
            {
                this.packetBroadcaster.QueuePacketTo(new SetScorePacket
                {
                    EntityName = s.DisplayText,
                    ObjectiveName = this.name,
                    Value = s.Value,
                }, players);
            }
        }
    }

    /// <remarks>
    /// Does nothing once the scoreboard is retired.
    /// </remarks>
    public bool RemoveScore(string scoreName)
    {
        lock (this.Gate)
        {
            if (this.retired || !this.scores.Remove(scoreName, out var score))
                return false;

            this.packetBroadcaster.QueuePacketTo(new SetScorePacket
            {
                EntityName = score.DisplayText,
                ObjectiveName = this.name,
            }, this.Players.ToArray());

            return true;
        }
    }

    public Score GetScore(string scoreName)
    {
        lock (this.Gate)
            return this.scores.GetValueOrDefault(scoreName);
    }

    /// <summary>
    /// Removes the objective from the players shown the scoreboard. Does nothing without one.
    /// </summary>
    public void RemoveObjective()
    {
        lock (this.Gate)
            this.SendObjectiveRemoval();
    }

    public ITeam CreateTeam(
        string name,
        ChatMessage displayName,
        NameTagVisibility nameTagVisibility,
        CollisionRule collisionRule,
        TeamColor color,
        params string[] entities)
    {
        var team = new Team(this, this.packetBroadcaster)
        {
            Name = name,
            DisplayName = displayName,
            NameTagVisibility = nameTagVisibility,
            CollisionRule = collisionRule,
            Color = color,
            Entities = entities.ToHashSet()
        };

        return this.AddTeam(team);
    }

    public ITeam CreateTeam(
        string name,
        ChatMessage displayName,
        NameTagVisibility nameTagVisibility,
        CollisionRule collisionRule,
        TeamColor color,
        ChatMessage prefix,
        ChatMessage suffix,
        params string[] entities)
    {
        var team = new Team(this, this.packetBroadcaster)
        {
            Name = name,
            DisplayName = displayName,
            NameTagVisibility = nameTagVisibility,
            CollisionRule = collisionRule,
            Color = color,
            Prefix = prefix,
            Suffix = suffix,
            Entities = entities.ToHashSet()
        };

        return this.AddTeam(team);
    }

    public void AddPlayer(int entityId)
    {
        lock (this.Gate)
        {
            this.ThrowIfRetired();
            this.Players.Add(entityId);
        }
    }

    /// <remarks>
    /// Does nothing once the scoreboard is retired: it's shown to no one then.
    /// </remarks>
    public bool RemovePlayer(int entityId)
    {
        lock (this.Gate)
            return this.Players.TryRemove(entityId);
    }

    /// <summary>
    /// Removes the objective and the teams from players' screens and refuses later changes, as the scoreboard's
    /// plugin unloads.
    /// </summary>
    internal void Retire()
    {
        lock (this.Gate)
        {
            if (this.retired)
                return;

            this.SendObjectiveRemoval();

            foreach (var team in this.Teams.OfType<Team>().ToArray())
                team.Delete();

            this.Teams.Clear();
            this.Players.Clear();
            this.retired = true;
        }
    }

    /// <summary>
    /// Shows the objective and its scores to a player shown the scoreboard (see <see cref="AddPlayer"/>), in
    /// <paramref name="slot"/>.
    /// </summary>
    /// <remarks>
    /// The packets go through the broadcaster, which runs plugins' packet handlers as it sends them, queued under
    /// <see cref="Gate"/> like the removals of <see cref="Retire"/>, <see cref="RemoveObjective"/> and
    /// <see cref="RemovePlayer"/>. So the player gets them in the order they happened: a removal is never overtaken
    /// by an earlier display, and never overtakes a later one.
    /// <para>
    /// It returns once they're queued, not sent: waiting for the broadcaster to send them could wait on the caller
    /// itself, as the broadcaster waits on the packet handlers it runs, and through them on whatever they wait for.
    /// </para>
    /// </remarks>
    /// <param name="entityId">The player's entity id.</param>
    /// <param name="slot">Where the objective shows.</param>
    /// <exception cref="InvalidOperationException">The scoreboard is retired or has no objective.</exception>
    internal void Display(int entityId, DisplaySlot slot)
    {
        lock (this.Gate)
        {
            this.ThrowIfRetired();

            var objective = this.Objective
                ?? throw new InvalidOperationException(
                    "You must create an objective for the scoreboard before displaying it.");

            if (!this.Players.Contains(entityId))
                return;

            // An array, as a lone int would be taken for the priority of QueuePacketTo's other overload.
            int[] to = [entityId];

            this.packetBroadcaster.QueuePacketTo(new SetObjectivePacket
            {
                ObjectiveName = this.name,
                Mode = ScoreboardMode.Create,
                Value = objective.Value,
                Type = objective.DisplayType
            }, to);

            foreach (var (_, score) in this.scores)
            {
                this.packetBroadcaster.QueuePacketTo(new SetScorePacket
                {
                    EntityName = score.DisplayText,
                    ObjectiveName = this.name,
                    Value = score.Value,
                }, to);
            }

            this.packetBroadcaster.QueuePacketTo(new SetDisplayObjectivePacket
            {
                ObjectiveName = this.name,
                DisplaySlot = slot
            }, to);
        }
    }

    /// <summary>
    /// Throws once the scoreboard is retired. Call under <see cref="Gate"/>.
    /// </summary>
    internal void ThrowIfRetired()
    {
        if (this.retired)
            throw new InvalidOperationException("The scoreboard's plugin unloaded.");
    }

    // Sends the players the objective's removal, as the objective goes; called under the lock.
    private void SendObjectiveRemoval()
    {
        if (this.Objective is null)
            return;

        var packet = new SetObjectivePacket
        {
            ObjectiveName = this.Objective.ObjectiveName,
            Mode = ScoreboardMode.Remove
        };

        this.packetBroadcaster.QueuePacketTo(packet, this.Players.ToArray());
        this.Objective = null;
    }

    private Team AddTeam(Team team)
    {
        lock (this.Gate)
        {
            this.ThrowIfRetired();

            team.Create();
            this.Teams.Add(team);
        }

        return team;
    }

    private void UpdateObjective(SetObjectivePacket packet)
    {
        var players = this.Players.ToArray();
        this.packetBroadcaster.QueuePacketTo(packet, players);

        foreach (var score in this.scores.Select(x => x.Value).OrderByDescending(x => x.Value))
        {
            this.packetBroadcaster.QueuePacketTo(new SetScorePacket
            {
                EntityName = score.DisplayText,
                ObjectiveName = this.name,
                Value = score.Value,
            }, players);
        }
    }
}
