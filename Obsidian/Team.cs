using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian;

public sealed class Team(IScoreboard scoreboard, IPacketBroadcaster packetBroadcaster) : ITeam
{
    private readonly IScoreboard scoreboard = scoreboard;
    private readonly IPacketBroadcaster packetBroadcaster = packetBroadcaster;

    private SetPlayerTeamPacket packet;
    public string Name { get; set; }

    public ChatMessage DisplayName { get; set; }
    public NameTagVisibility NameTagVisibility { get; set; }
    public CollisionRule CollisionRule { get; set; }
    public TeamColor Color { get; set; }
    public ChatMessage Prefix { get; set; }
    public ChatMessage Suffix { get; set; }

    public HashSet<string> Entities { get; set; }

    public void Create()
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

        this.packetBroadcaster.Broadcast(this.packet);
    }

    public int AddEntities(params string[] entities)
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

        this.packetBroadcaster.Broadcast(this.packet);

        this.packet.Entities.Clear();

        return added;
    }

    public int RemoveEntities(params string[] entities)
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

        this.packetBroadcaster.Broadcast(this.packet);

        this.packet.Entities.Clear();

        return removed;
    }

    public void Delete()
    {
        this.SetPacketMode(TeamModeOption.RemoveTeam);

        this.packetBroadcaster.Broadcast(this.packet);

        this.scoreboard.Teams.Remove(this);
    }

    public void Update()
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

        this.packetBroadcaster.Broadcast(this.packet);
    }

    private void SetPacketMode(TeamModeOption mode) => this.packet.Mode = mode;
}
