using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Scoreboard;

namespace Obsidian;

public class Scoreboard(string name, IPacketBroadcaster packetBroadcaster, IServer server) : IScoreboard
{
    private readonly IPacketBroadcaster packetBroadcaster = packetBroadcaster;
    private readonly IServer server = server;

    internal readonly string name = name;
    internal readonly Dictionary<string, Score> scores = [];

    public ConcurrentHashSet<int> Players { get; } = [];

    public ScoreboardObjective Objective { get; private set; }

    public List<ITeam> Teams { get; set; } = [];

    public void CreateOrUpdateObjective(ChatMessage title, DisplayType displayType = DisplayType.Integer)
    {
        var packet = new SetObjectivePacket
        {
            ObjectiveName = this.name,
            Mode = this.Objective != null ? ScoreboardMode.Update : ScoreboardMode.Create,
            Value = title,
            Type = displayType
        };

        if (this.Objective != null)
        {
            this.UpdateObjective(packet);
        }
        else
        {
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

    public bool RemoveScore(string scoreName)
    {
        if (this.scores.Remove(scoreName, out var score))
        {
            this.packetBroadcaster.QueuePacketTo(new SetScorePacket
            {
                EntityName = score.DisplayText,
                ObjectiveName = this.name,
            }, this.Players.ToArray());

            return true;
        }

        return false;
    }

    public Score GetScore(string scoreName) => this.scores.GetValueOrDefault(scoreName);

    public void RemoveObjective()
    {
        var obj = new SetObjectivePacket
        {
            ObjectiveName = this.Objective.ObjectiveName,
            Mode = ScoreboardMode.Remove
        };

        this.packetBroadcaster.Broadcast(obj, this.Players.ToArray());
    }

    private void UpdateObjective(SetObjectivePacket packet)
    {
        var players = this.Players.ToArray();
        this.packetBroadcaster.QueuePacketTo(packet, players);

        foreach (var score in this.scores.Select(x => x.Value).OrderByDescending(x => x.Value))
        {
            this.packetBroadcaster.Broadcast(new SetScorePacket
            {
                EntityName = score.DisplayText,
                ObjectiveName = this.name,
                Value = score.Value,
            }, players);
        }
    }

    public ITeam CreateTeam(string name, ChatMessage displayName, NameTagVisibility nameTagVisibility, CollisionRule collisionRule,
        TeamColor color, params string[] entities)
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

        team.Create();

        this.Teams.Add(team);

        return team;
    }
    public ITeam CreateTeam(string name, ChatMessage displayName, NameTagVisibility nameTagVisibility, CollisionRule collisionRule,
        TeamColor color, ChatMessage prefix, ChatMessage suffix, params string[] entities)
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

        team.Create();

        this.Teams.Add(team);

        return team;
    }

    public void AddPlayer(int entityId) => this.Players.Add(entityId);
    public bool RemovePlayer(int entityId) => this.Players.TryRemove(entityId);
}
