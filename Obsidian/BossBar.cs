using Obsidian.API.Boss;
using Obsidian.Entities;
using Obsidian.Net.Actions.BossBar;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian;

public class BossBar : IBossBar
{
    private Server server;

    private BossBarRemoveAction removeAction;
    public HashSet<Guid> Players { get; } = new();

    public Guid Uuid { get; } = Guid.NewGuid();

    public ChatMessage Title { get; set; }

    public float Health { get; set; }

    public BossBarColor Color { get; set; }

    public BossBarDivisionType DivisionType { get; set; }

    public BossBarFlags Flags { get; set; }

    public BossBar(Server server)
    {
        this.server = server;
        removeAction = new BossBarRemoveAction
        {
            Uuid = Uuid
        };
    }

    public async Task RemoveFlagAsync(BossBarFlags flag)
    {
        Flags &= ~flag;

        await UpdateFlagsAsync();
    }

    public async Task UpdateColorAsync(BossBarColor newColor)
    {
        Color = newColor;

        await UpdateStyleAsync();
    }

    public async Task UpdateDivisionAsync(BossBarDivisionType newDivision)
    {
        DivisionType = newDivision;

        await UpdateStyleAsync();
    }

    public async Task AddFlagsAsync(BossBarFlags newFlags)
    {
        Flags |= newFlags;

        await UpdateFlagsAsync();
    }

    public async Task UpdateHealthAsync(float newHealth)
    {
        Health = newHealth;

        var updateHealthAction = new BossBarUpdateHealthAction
        {
            Uuid = Uuid,
            Health = Health
        };

        foreach (var (uuid, player) in server.OnlinePlayers)
        {
            if (!HasPlayer(uuid))
                continue;

            await player.client.QueuePacketAsync(new BossBarPacket(updateHealthAction));
        }
    }

    public async Task UpdateTitleAsync(ChatMessage newTitle)
    {
        Title = newTitle;

        var updateHealthAction = new BossBarUpdateTitleAction
        {
            Uuid = Uuid,
            Title = Title
        };

        foreach (var (uuid, player) in server.OnlinePlayers)
        {
            if (!HasPlayer(uuid))
                continue;

            await player.client.QueuePacketAsync(new BossBarPacket(updateHealthAction));
        }
    }

    public async Task AddPlayerAsync(Guid playerUuid)
    {
        var hasPlayer = Players.Add(playerUuid);

        //Players already in the list so we assume they're seeing the bar.
        if (!hasPlayer)
            return;

        var addAction = new BossBarAddAction
        {
            Uuid = Uuid,
            Title = Title,
            Color = Color,
            Division = DivisionType,
            Flags = Flags,
            Health = Health
        };

        var player = server.GetPlayer(playerUuid) as Player;

        await player.client.QueuePacketAsync(new BossBarPacket(addAction));
    }

    public async Task RemovePlayerAsync(Guid playerUuid)
    {
        var removed = Players.Remove(playerUuid);

        //Player is not in here??
        if (!removed)
            return;

        var player = server.GetPlayer(playerUuid) as Player;

        await player.client.QueuePacketAsync(new BossBarPacket(removeAction));
    }

    public bool HasPlayer(Guid uuid) => Players.Contains(uuid);

    private async Task UpdateFlagsAsync()
    {
        var updateFlagAction = new BossBarUpdateFlagsAction
        {
            Uuid = Uuid,
            Flags = Flags
        };

        foreach (var (uuid, player) in server.OnlinePlayers)
        {
            if (!HasPlayer(uuid))
                continue;

            await player.client.QueuePacketAsync(new BossBarPacket(updateFlagAction));
        }
    }

    private async Task UpdateStyleAsync()
    {
        var updateStyleAction = new BossBarUpdateStyleAction
        {
            Uuid = Uuid,
            Color = Color,
            Division = DivisionType
        };

        foreach (var (uuid, player) in server.OnlinePlayers)
        {
            if (!HasPlayer(uuid))
                continue;

            await player.client.QueuePacketAsync(new BossBarPacket(updateStyleAction));
        }
    }
}
