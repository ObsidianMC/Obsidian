using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Scoreboard;

namespace Obsidian;

public class Scoreboard : IScoreboard
{
    private readonly Server server;

    internal readonly string name;

    internal readonly Dictionary<string, Score> scores = new Dictionary<string, Score>();

    public ScoreboardObjective Objective { get; private set; }

    public List<ITeam> Teams { get; set; } = new();

    public Scoreboard(string name, Server server)
    {
        this.name = name;
        this.server = server;
    }

    public async Task CreateOrUpdateObjectiveAsync(ChatMessage title, DisplayType displayType = DisplayType.Integer)//TODO impl new scoreboard stuff
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
            await this.UpdateObjectiveAsync(packet);
        }
        else
        {
            this.Objective = new ScoreboardObjective
            (
                this.name,
                title,
                displayType
            );

            foreach (var (_, player) in this.server.OnlinePlayers)
            {
                if (player.CurrentScoreboard == this)
                {
                    await player.Client.QueuePacketAsync(packet);

                    foreach (var score in this.scores.Select(x => x.Value).OrderByDescending(x => x.Value))
                    {
                        await player.Client.QueuePacketAsync(new SetScorePacket
                        {
                            EntityName = score.DisplayText,
                            ObjectiveName = this.name,
                            Value = score.Value
                        });
                    }
                }
            }
        }
    }

    //TODO impl new scoreboard stuff
    public async Task CreateOrUpdateScoreAsync(string scoreName, string displayText, int? value = null)
    {
        var score = new Score(displayText, value ?? 0);

        if (this.scores.TryGetValue(scoreName, out var cachedScore))
        {
            score = cachedScore;

            if (value.HasValue)
                score.Value = (int)value;

            foreach (var (_, player) in this.server.OnlinePlayers)
            {
                if (player.CurrentScoreboard != this)
                    continue;

                await player.Client.QueuePacketAsync(new SetScorePacket
                {
                    EntityName = score.DisplayText,
                    ObjectiveName = this.name,
                });
            }

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

        foreach (var (_, player) in this.server.OnlinePlayers)
        {
            if (player.CurrentScoreboard != this)
                continue;

            foreach (var (_, s) in this.scores.OrderBy(x => x.Value.Value))
            {
                await player.Client.QueuePacketAsync(new SetScorePacket
                {
                    EntityName = s.DisplayText,
                    ObjectiveName = this.name,
                    Value = s.Value,
                });
            }
        }

    }

    //TODO impl new scoreboard stuff
    public async Task<bool> RemoveScoreAsync(string scoreName)
    {
        if (this.scores.Remove(scoreName, out var score))
        {
            foreach (var (_, player) in this.server.OnlinePlayers)
            {
                if (player.CurrentScoreboard != this)
                    continue;

                await player.Client.QueuePacketAsync(new SetScorePacket
                {
                    EntityName = score.DisplayText,
                    ObjectiveName = this.name,
                });
            }

            return true;
        }

        return false;
    }

    public Score GetScore(string scoreName) => this.scores.GetValueOrDefault(scoreName);

    public async Task RemoveObjectiveAsync()
    {
        var obj = new SetObjectivePacket
        {
            ObjectiveName = this.Objective.ObjectiveName,
            Mode = ScoreboardMode.Remove
        };

        foreach (var (_, player) in this.server.OnlinePlayers)
        {
            if (player.CurrentScoreboard == this)
                await player.Client.QueuePacketAsync(obj);
        }
    }

    //TODO impl new scoreboard stuff
    private async Task UpdateObjectiveAsync(SetObjectivePacket packet)
    {
        foreach (var (_, player) in this.server.OnlinePlayers)
        {
            if (player.CurrentScoreboard == this)
            {
                await player.Client.QueuePacketAsync(packet);

                foreach (var score in this.scores.Select(x => x.Value).OrderByDescending(x => x.Value))
                {
                    await player.Client.QueuePacketAsync(new SetScorePacket
                    {
                        EntityName = score.DisplayText,
                        ObjectiveName = this.name,
                        Value = score.Value,
                    });
                }
            }
        }
    }

    public async Task<ITeam> CreateTeamAsync(string name, ChatMessage displayName, NameTagVisibility nameTagVisibility, CollisionRule collisionRule,
        TeamColor color, params string[] entities)
    {
        var team = new Team(this, this.server)
        {
            Name = name,
            DisplayName = displayName,
            NameTagVisibility = nameTagVisibility,
            CollisionRule = collisionRule,
            Color = color,
            Entities = entities.ToHashSet()
        };

        await team.CreateAsync();

        this.Teams.Add(team);

        return team;
    }
    public async Task<ITeam> CreateTeamAsync(string name, ChatMessage displayName, NameTagVisibility nameTagVisibility, CollisionRule collisionRule,
        TeamColor color, ChatMessage prefix, ChatMessage suffix, params string[] entities)
    {
        var team = new Team(this, this.server)
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

        await team.CreateAsync();

        this.Teams.Add(team);

        return team;
    }
}
