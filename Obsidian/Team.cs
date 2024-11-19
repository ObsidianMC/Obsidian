using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian;

public sealed class Team : ITeam
{
    private readonly Scoreboard scoreboard;
    private readonly Server server;

    private SetPlayerTeamPacket packet;
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
        this.packet = new()
        {
            TeamName = this.Name,
            TeamDisplayName = this.DisplayName,
            NameTagVisibility = this.NameTagVisibility,
            CollisionRule = this.CollisionRule,
            TeamColor = this.Color,
            TeamPrefix = this.Prefix,
            TeamSuffix = this.Suffix,
            Entities = this.Entities
        };

        await this.server.QueueBroadcastPacketAsync(this.packet);
    }

    public async Task<int> AddEntitiesAsync(params string[] entities)
    {
        this.SetPacketMode(TeamModeOption.AddEntities);

        var added = 0;

        foreach (var entity in entities)
        {
            if (this.Entities.Add(entity))
            {
                this.packet.Entities.Add(entity);
                added++;
            }
        }

        await this.server.QueueBroadcastPacketAsync(this.packet);

        this.packet.Entities.Clear();

        return added;
    }

    public async Task<int> RemoveEntitiesAsync(params string[] entities)
    {
        this.SetPacketMode(TeamModeOption.RemoveEntities);

        var removed = 0;
        foreach (var entity in entities)
        {
            if (this.Entities.Remove(entity))
            {
                this.packet.Entities.Add(entity);
                removed++;
            }
        }

        await this.server.QueueBroadcastPacketAsync(this.packet);

        this.packet.Entities.Clear();

        return removed;
    }

    public async Task DeleteAsync()
    {
        this.SetPacketMode(TeamModeOption.RemoveTeam);

        await this.server.QueueBroadcastPacketAsync(this.packet);

        this.scoreboard.Teams.Remove(this);
    }

    public async Task UpdateAsync()
    {
        this.packet = new()
        {
            TeamName = this.Name,
            Mode = TeamModeOption.UpdateTeam,
            TeamDisplayName = this.DisplayName,
            NameTagVisibility = this.NameTagVisibility,
            CollisionRule = this.CollisionRule,
            TeamColor = this.Color,
            TeamPrefix = this.Prefix,
            TeamSuffix = this.Suffix,
            Entities = this.Entities
        };

        await this.server.QueueBroadcastPacketAsync(this.packet);
    }

    private void SetPacketMode(TeamModeOption mode) => this.packet.Mode = mode;
}
