using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian;

public class Team : ITeam
{
    private readonly Scoreboard scoreboard;
    private readonly Server server;

    private TeamsPacket packet;
    public string Name { get; set; }

    public ChatMessage DisplayName { get; set; }
    public NameTagVisibility NameTagVisibility { get; set; }
    public CollisionRule CollisionRule { get; set; }
    public TeamColor Color { get; set; }
    public ChatMessage Prefix { get; set; }
    public ChatMessage Suffix { get; set; }

    public HashSet<string> Entities { get; set; }

    public Team(Scoreboard scoreboard, Server server)
    {
        this.scoreboard = scoreboard;
        this.server = server;
    }

    public async Task CreateAsync()
    {
        packet = new()
        {
            TeamName = Name,
            TeamDisplayName = DisplayName,
            NameTagVisibility = NameTagVisibility,
            CollisionRule = CollisionRule,
            TeamColor = Color,
            TeamPrefix = Prefix,
            TeamSuffix = Suffix,
            Entities = Entities
        };

        await server.QueueBroadcastPacketAsync(packet);
    }

    public async Task<int> AddEntitiesAsync(params string[] entities)
    {
        SetPacketMode(TeamModeOption.AddEntities);

        var added = 0;

        foreach (var entity in entities)
        {
            if (Entities.Add(entity))
            {
                packet.Entities.Add(entity);
                added++;
            }
        }

        await server.QueueBroadcastPacketAsync(packet);

        packet.Entities.Clear();

        return added;
    }

    public async Task<int> RemoveEntitiesAsync(params string[] entities)
    {
        SetPacketMode(TeamModeOption.RemoveEntities);

        var removed = 0;
        foreach (var entity in entities)
        {
            if (Entities.Remove(entity))
            {
                packet.Entities.Add(entity);
                removed++;
            }
        }

        await server.QueueBroadcastPacketAsync(packet);

        packet.Entities.Clear();

        return removed;
    }

    public async Task DeleteAsync()
    {
        SetPacketMode(TeamModeOption.RemoveTeam);

        await server.QueueBroadcastPacketAsync(packet);

        scoreboard.Teams.Remove(this);
    }

    public async Task UpdateAsync()
    {
        packet = new()
        {
            TeamName = Name,
            Mode = TeamModeOption.UpdateTeam,
            TeamDisplayName = DisplayName,
            NameTagVisibility = NameTagVisibility,
            CollisionRule = CollisionRule,
            TeamColor = Color,
            TeamPrefix = Prefix,
            TeamSuffix = Suffix,
            Entities = Entities
        };

        await server.QueueBroadcastPacketAsync(packet);
    }

    private void SetPacketMode(TeamModeOption mode) => packet.Mode = mode;
}
