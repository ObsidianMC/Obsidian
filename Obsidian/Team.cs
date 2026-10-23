using Obsidian.Net.Packets.Play.Clientbound;
using System.Threading;

namespace Obsidian;

/// <remarks>
/// A team of a retired <see cref="Scoreboard"/> is deleted with it, and changing it throws
/// <see cref="InvalidOperationException"/>.
/// </remarks>
public sealed class Team(IScoreboard scoreboard, IPacketBroadcaster packetBroadcaster) : ITeam
{
    private readonly IScoreboard scoreboard = scoreboard;
    private readonly IPacketBroadcaster packetBroadcaster = packetBroadcaster;

    // The scoreboard's lock, so the team changes in step with the scoreboard's retirement, or its own otherwise.
    private readonly Lock gate = (scoreboard as Scoreboard)?.Gate ?? new();

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
        lock (this.gate)
        {
            this.ThrowIfRetired();

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
    }

    public int AddEntities(params string[] entities)
    {
        lock (this.gate)
        {
            this.ThrowIfRetired();
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
    }

    /// <remarks>
    /// Does nothing once the scoreboard is retired.
    /// </remarks>
    public int RemoveEntities(params string[] entities)
    {
        lock (this.gate)
        {
            if (this.IsRetired)
                return 0;

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
    }

    /// <remarks>
    /// Does nothing once the scoreboard is retired, which deleted the team.
    /// </remarks>
    public void Delete()
    {
        lock (this.gate)
        {
            if (this.IsRetired)
                return;

            this.SetPacketMode(TeamModeOption.RemoveTeam);

            this.packetBroadcaster.Broadcast(this.packet);

            this.scoreboard.Teams.Remove(this);
        }
    }

    public void Update()
    {
        lock (this.gate)
        {
            this.ThrowIfRetired();

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
    }

    private bool IsRetired => this.scoreboard is Scoreboard { IsRetired: true };

    private void ThrowIfRetired()
    {
        if (this.IsRetired)
            throw new InvalidOperationException("The team's scoreboard's plugin unloaded.");
    }

    private void SetPacketMode(TeamModeOption mode) => this.packet.Mode = mode;

    /// <summary>A packet creating the team as it is now, for players who join after it was created.</summary>
    internal SetPlayerTeamPacket CreationPacket()
    {
        lock (this.gate)
        {
            return new()
            {
                TeamName = this.Name,
                Mode = TeamModeOption.CreateTeam,
                TeamDisplayName = this.DisplayName,
                NameTagVisibility = this.NameTagVisibility,
                CollisionRule = this.CollisionRule,
                TeamColor = this.Color,
                TeamPrefix = this.Prefix,
                TeamSuffix = this.Suffix,
                Entities = [.. this.Entities]
            };
        }
    }
}
