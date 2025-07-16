using Obsidian.API.Boss;
using Obsidian.Net.Actions.BossBar;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian;

public class BossBar : IBossBar
{
    private readonly BossBarRemoveAction removeAction;
    private readonly IPacketBroadcaster packetBroadcaster;

    public HashSet<int> Players { get; } = [];

    public Guid Uuid { get; } = Guid.NewGuid();

    public ChatMessage Title { get; set; }

    public float Health { get; set; }

    public BossBarColor Color { get; set; }

    public BossBarDivisionType DivisionType { get; set; }

    public BossBarFlags Flags { get; set; }

    public BossBar(IPacketBroadcaster packetBroadcaster, ChatMessage title, float health, BossBarColor color, BossBarDivisionType divisionType, BossBarFlags flags)
    {
        this.removeAction = new BossBarRemoveAction
        {
            Uuid = this.Uuid
        };
        this.packetBroadcaster = packetBroadcaster;
        this.Title = title;
        this.Health = health;
        this.Color = color;
        this.DivisionType = divisionType;
        this.Flags = flags;
    }

    public void RemoveFlag(BossBarFlags flag)
    {
        this.Flags &= ~flag;

        this.UpdateFlags();
    }

    public void UpdateColor(BossBarColor newColor)
    {
        this.Color = newColor;

        this.UpdateStyle();
    }

    public void UpdateDivision(BossBarDivisionType newDivision)
    {
        this.DivisionType = newDivision;

        this.UpdateStyle();
    }

    public void AddFlags(BossBarFlags newFlags)
    {
        this.Flags |= newFlags;

        this.UpdateFlags();
    }

    public void UpdateHealth(float newHealth)
    {
        this.Health = newHealth;

        var updateHealthAction = new BossBarUpdateHealthAction
        {
            Uuid = this.Uuid,
            Health = this.Health
        };

        this.packetBroadcaster.QueuePacketTo(new BossEventPacket(updateHealthAction), this.Players.ToArray());
    }

    public void UpdateTitle(ChatMessage newTitle)
    {
        this.Title = newTitle;

        var updateHealthAction = new BossBarUpdateTitleAction
        {
            Uuid = this.Uuid,
            Title = this.Title
        };

        this.packetBroadcaster.QueuePacketTo(new BossEventPacket(updateHealthAction), this.Players.ToArray());
    }

    public bool HasPlayer(int entityID) => this.Players.Contains(entityID);

    public void AddPlayer(int entityID)
    {
        var hasPlayer = this.Players.Add(entityID);

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

        this.packetBroadcaster.QueuePacketTo(new BossEventPacket(addAction), entityID);
    }

    public void RemovePlayer(int entityId)
    {
        var removed = this.Players.Remove(entityId);

        //Player is not in here??
        if (!removed)
            return;

        this.packetBroadcaster.QueuePacketTo(new BossEventPacket(this.removeAction), entityId);
    }

    private void UpdateFlags()
    {
        var updateFlagAction = new BossBarUpdateFlagsAction
        {
            Uuid = this.Uuid,
            Flags = this.Flags
        };

        this.packetBroadcaster.QueuePacketTo(new BossEventPacket(updateFlagAction), this.Players.ToArray());
    }

    private void UpdateStyle()
    {
        var updateStyleAction = new BossBarUpdateStyleAction
        {
            Uuid = this.Uuid,
            Color = this.Color,
            Division = this.DivisionType
        };

        this.packetBroadcaster.QueuePacketTo(new BossEventPacket(updateStyleAction), this.Players.ToArray());
    }
}
