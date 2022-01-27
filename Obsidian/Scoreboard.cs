using Obsidian.Net.Packets.Play.Clientbound;

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

    public async Task CreateOrUpdateObjectiveAsync(ChatMessage title, DisplayType displayType = DisplayType.Integer)
    {
        var packet = new ScoreboardObjectivePacket
        {
            ObjectiveName = name,
            Mode = Objective != null ? ScoreboardMode.Update : ScoreboardMode.Create,
            Value = title,
            Type = displayType
        };

        if (Objective != null)
        {
            await UpdateObjectiveAsync(packet);
        }
        else
        {
            Objective = new ScoreboardObjective
            (
                name,
                title,
                displayType
            );

            foreach (var (_, player) in server.OnlinePlayers)
            {
                if (player.CurrentScoreboard == this)
                {
                    await player.client.QueuePacketAsync(packet);

                    foreach (var score in scores.Select(x => x.Value).OrderByDescending(x => x.Value))
                    {
                        await player.client.QueuePacketAsync(new UpdateScore
                        {
                            EntityName = score.DisplayText,
                            ObjectiveName = name,
                            Action = 0,
                            Value = score.Value
                        });
                    }
                }
            }
        }
    }

    public async Task CreateOrUpdateScoreAsync(string scoreName, string displayText, int? value = null)
    {
        var score = new Score(displayText, value ?? 0);

        if (scores.TryGetValue(scoreName, out var cachedScore))
        {
            score = cachedScore;

            if (value.HasValue)
                score.Value = (int)value;

            foreach (var (_, player) in server.OnlinePlayers)
            {
                if (player.CurrentScoreboard != this)
                    continue;

                await player.client.QueuePacketAsync(new UpdateScore
                {
                    EntityName = score.DisplayText,
                    ObjectiveName = name,
                    Action = 1,
                });
            }

            score.DisplayText = displayText;
        }
        else
        {
            if (scores.Count > 0)
            {
                score.Value = scores.Select(x => x.Value).OrderByDescending(x => x.Value).Last().Value;

                foreach (var element in scores.Select(x => x.Value).OrderByDescending(x => x.Value))
                    element.Value += 1;
            }

            scores[scoreName] = score;
        }

        foreach (var (_, player) in server.OnlinePlayers)
        {
            if (player.CurrentScoreboard != this)
                continue;

            foreach (var (_, s) in scores.OrderBy(x => x.Value.Value))
            {
                await player.client.QueuePacketAsync(new UpdateScore
                {
                    EntityName = s.DisplayText,
                    ObjectiveName = name,
                    Action = 0,
                    Value = s.Value
                });
            }
        }

    }

    public async Task<bool> RemoveScoreAsync(string scoreName)
    {
        if (scores.Remove(scoreName, out var score))
        {
            foreach (var (_, player) in server.OnlinePlayers)
            {
                if (player.CurrentScoreboard != this)
                    continue;

                await player.client.QueuePacketAsync(new UpdateScore
                {
                    EntityName = score.DisplayText,
                    ObjectiveName = name,
                    Action = 1,
                });
            }

            return true;
        }

        return false;
    }

    public Score GetScore(string scoreName) => scores.GetValueOrDefault(scoreName);

    public async Task RemoveObjectiveAsync()
    {
        var obj = new ScoreboardObjectivePacket
        {
            ObjectiveName = Objective.ObjectiveName,
            Mode = ScoreboardMode.Remove
        };

        foreach (var (_, player) in server.OnlinePlayers)
        {
            if (player.CurrentScoreboard == this)
                await player.client.QueuePacketAsync(obj);
        }
    }

    private async Task UpdateObjectiveAsync(ScoreboardObjectivePacket packet)
    {
        foreach (var (_, player) in server.OnlinePlayers)
        {
            if (player.CurrentScoreboard == this)
            {
                await player.client.QueuePacketAsync(packet);

                foreach (var score in scores.Select(x => x.Value).OrderByDescending(x => x.Value))
                {
                    await player.client.QueuePacketAsync(new UpdateScore
                    {
                        EntityName = score.DisplayText,
                        ObjectiveName = name,
                        Action = 0,
                        Value = score.Value
                    });
                }
            }
        }
    }

    public async Task<ITeam> CreateTeamAsync(string name, ChatMessage displayName, NameTagVisibility nameTagVisibility, CollisionRule collisionRule,
        TeamColor color, params string[] entities)
    {
        var team = new Team(this, server)
        {
            Name = name,
            DisplayName = displayName,
            NameTagVisibility = nameTagVisibility,
            CollisionRule = collisionRule,
            Color = color,
            Entities = entities.ToHashSet()
        };

        await team.CreateAsync();

        Teams.Add(team);

        return team;
    }
    public async Task<ITeam> CreateTeamAsync(string name, ChatMessage displayName, NameTagVisibility nameTagVisibility, CollisionRule collisionRule,
        TeamColor color, ChatMessage prefix, ChatMessage suffix, params string[] entities)
    {
        var team = new Team(this, server)
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

        Teams.Add(team);

        return team;
    }
}
