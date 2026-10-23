using Obsidian.API.Boss;
using Obsidian.Net.Actions.BossBar;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Plugins;
using System.Threading;

namespace Obsidian;

/// <remarks>
/// A plugin's boss bar is retired when the plugin unloads (see <see cref="Retire"/>): it's hidden, and changing it
/// or showing it again throws <see cref="InvalidOperationException"/>, so a handler still running can't bring it
/// back.
/// </remarks>
public class BossBar : IBossBar
{
    private readonly BossBarRemoveAction removeAction;
    private readonly IPacketBroadcaster packetBroadcaster;

    // Taken by every change, which sends its packets before letting go, so none is sent after Retire hid the bar.
    private readonly Lock gate = new();
    private bool retired;

    /// <summary>
    /// The entity ids of the players shown the bar. Change it with <see cref="AddPlayer"/> and
    /// <see cref="RemovePlayer"/>.
    /// </summary>
    public HashSet<int> Players { get; } = [];

    public Guid Uuid { get; } = Guid.NewGuid();

    public ChatMessage Title { get; set; }

    public float Health { get; set; }

    public BossBarColor Color { get; set; }

    public BossBarDivisionType DivisionType { get; set; }

    public BossBarFlags Flags { get; set; }

    /// <summary>
    /// The plugin's boss bars this one is among, told whether it's shown so that it's kept while it is.
    /// </summary>
    internal PluginBossBars? Owner { get; set; }

    public BossBar(IPacketBroadcaster packetBroadcaster, ChatMessage title, float health, BossBarColor color,
        BossBarDivisionType divisionType, BossBarFlags flags)
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
        lock (this.gate)
        {
            this.ThrowIfRetired();
            this.Flags &= ~flag;
            this.UpdateFlags();
        }
    }

    public void UpdateColor(BossBarColor newColor)
    {
        lock (this.gate)
        {
            this.ThrowIfRetired();
            this.Color = newColor;
            this.UpdateStyle();
        }
    }

    public void UpdateDivision(BossBarDivisionType newDivision)
    {
        lock (this.gate)
        {
            this.ThrowIfRetired();
            this.DivisionType = newDivision;
            this.UpdateStyle();
        }
    }

    public void AddFlags(BossBarFlags newFlags)
    {
        lock (this.gate)
        {
            this.ThrowIfRetired();
            this.Flags |= newFlags;
            this.UpdateFlags();
        }
    }

    public void UpdateHealth(float newHealth)
    {
        lock (this.gate)
        {
            this.ThrowIfRetired();
            this.Health = newHealth;

            var updateHealthAction = new BossBarUpdateHealthAction
            {
                Uuid = this.Uuid,
                Health = this.Health
            };

            this.packetBroadcaster.QueuePacketTo(new BossEventPacket(updateHealthAction), this.Players.ToArray());
        }
    }

    public void UpdateTitle(ChatMessage newTitle)
    {
        lock (this.gate)
        {
            this.ThrowIfRetired();
            this.Title = newTitle;

            var updateTitleAction = new BossBarUpdateTitleAction
            {
                Uuid = this.Uuid,
                Title = this.Title
            };

            this.packetBroadcaster.QueuePacketTo(new BossEventPacket(updateTitleAction), this.Players.ToArray());
        }
    }

    public bool HasPlayer(int entityID)
    {
        lock (this.gate)
            return this.Players.Contains(entityID);
    }

    public void AddPlayer(int entityID)
    {
        lock (this.gate)
        {
            this.ThrowIfRetired();

            //Players already in the list so we assume they're seeing the bar.
            if (!this.Players.Add(entityID))
                return;

            if (this.Players.Count == 1)
                this.Owner?.SetShown(this, true);

            var addAction = new BossBarAddAction
            {
                Uuid = this.Uuid,
                Title = this.Title,
                Color = this.Color,
                Division = this.DivisionType,
                Flags = this.Flags,
                Health = this.Health
            };

            this.packetBroadcaster.QueuePacketTo(new BossEventPacket(addAction), ids: [entityID]);
        }
    }

    /// <remarks>
    /// Does nothing once the bar is retired: it's shown to no one then.
    /// </remarks>
    public void RemovePlayer(int entityId)
    {
        lock (this.gate)
        {
            //Player is not in here??
            if (!this.Players.Remove(entityId))
                return;

            if (this.Players.Count == 0)
                this.Owner?.SetShown(this, false);

            this.packetBroadcaster.QueuePacketTo(new BossEventPacket(this.removeAction), ids: [entityId]);
        }
    }

    /// <summary>
    /// Hides the bar from its players and refuses its later changes, as its plugin unloads.
    /// </summary>
    internal void Retire()
    {
        lock (this.gate)
        {
            if (this.retired)
                return;

            this.retired = true;

            var players = this.Players.ToArray();
            this.Players.Clear();

            if (players.Length > 0)
                this.packetBroadcaster.QueuePacketTo(new BossEventPacket(this.removeAction), players);
        }
    }

    private void ThrowIfRetired()
    {
        if (this.retired)
            throw new InvalidOperationException("The boss bar's plugin unloaded.");
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
