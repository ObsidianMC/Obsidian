using Obsidian.API.Boss;
using Obsidian.Entities;
using Obsidian.Net.Actions.BossBar;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian;

public class BossBar : IBossBar
{
    private Server server;

    private BossBarRemoveAction removeAction;
    public HashSet<Guid> Players { get; } = [];

    public Guid Uuid { get; } = Guid.NewGuid();

    public ChatMessage Title { get; set; }

    public float Health { get; set; }

    public BossBarColor Color { get; set; }

    public BossBarDivisionType DivisionType { get; set; }

    public BossBarFlags Flags { get; set; }

    public BossBar(Server server)
    {
        this.server = server;
        this.removeAction = new BossBarRemoveAction
        {
            Uuid = this.Uuid
        };
    }

    public async Task RemoveFlagAsync(BossBarFlags flag)
    {
        this.Flags &= ~flag;

        await this.UpdateFlagsAsync();
    }

    public async Task UpdateColorAsync(BossBarColor newColor)
    {
        this.Color = newColor;

        await this.UpdateStyleAsync();
    }

    public async Task UpdateDivisionAsync(BossBarDivisionType newDivision)
    {
        this.DivisionType = newDivision;

        await this.UpdateStyleAsync();
    }

    public async Task AddFlagsAsync(BossBarFlags newFlags)
    {
        this.Flags |= newFlags;

        await this.UpdateFlagsAsync();
    }

    public async Task UpdateHealthAsync(float newHealth)
    {
        this.Health = newHealth;

        var updateHealthAction = new BossBarUpdateHealthAction
        {
            Uuid = this.Uuid,
            Health = this.Health
        };

        foreach (var (uuid, player) in this.server.OnlinePlayers)
        {
            if (!this.HasPlayer(uuid))
                continue;

            await player.Client.QueuePacketAsync(new BossEventPacket(updateHealthAction));
        }
    }

    public async Task UpdateTitleAsync(ChatMessage newTitle)
    {
        this.Title = newTitle;

        var updateHealthAction = new BossBarUpdateTitleAction
        {
            Uuid = this.Uuid,
            Title = this.Title
        };

        foreach (var (uuid, player) in this.server.OnlinePlayers)
        {
            if (!this.HasPlayer(uuid))
                continue;

            await player.Client.QueuePacketAsync(new BossEventPacket(updateHealthAction));
        }
    }

    public async Task AddPlayerAsync(Guid playerUuid)
    {
        var hasPlayer = this.Players.Add(playerUuid);

        //Players already in the list so we assume they're seeing the bar.
        if (!hasPlayer)
            return;

        var addAction = new BossBarAddAction
        {
            Uuid = this.Uuid,
            Title = this.Title,
            Color = this.Color,
            Division = this.DivisionType,
            Flags = this.Flags,
            Health = this.Health
        };

        var player = this.server.GetPlayer(playerUuid) as Player;

        await player.Client.QueuePacketAsync(new BossEventPacket(addAction));
    }

    public async Task RemovePlayerAsync(Guid playerUuid)
    {
        var removed = this.Players.Remove(playerUuid);

        //Player is not in here??
        if (!removed)
            return;

        var player = this.server.GetPlayer(playerUuid) as Player;

        await player.Client.QueuePacketAsync(new BossEventPacket(this.removeAction));
    }

    public bool HasPlayer(Guid uuid) => this.Players.Contains(uuid);

    private async Task UpdateFlagsAsync()
    {
        var updateFlagAction = new BossBarUpdateFlagsAction
        {
            Uuid = this.Uuid,
            Flags = this.Flags
        };

        foreach (var (uuid, player) in this.server.OnlinePlayers)
        {
            if (!this.HasPlayer(uuid))
                continue;

            await player.Client.QueuePacketAsync(new BossEventPacket(updateFlagAction));
        }
    }

    private async Task UpdateStyleAsync()
    {
        var updateStyleAction = new BossBarUpdateStyleAction
        {
            Uuid = this.Uuid,
            Color = this.Color,
            Division = this.DivisionType
        };

        foreach (var (uuid, player) in this.server.OnlinePlayers)
        {
            if (!this.HasPlayer(uuid))
                continue;

            await player.Client.QueuePacketAsync(new BossEventPacket(updateStyleAction));
        }
    }
}
