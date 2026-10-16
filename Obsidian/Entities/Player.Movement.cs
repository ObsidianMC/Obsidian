using Obsidian.API.Inventory;
using Obsidian.Net.Packets.Common;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Payloads;
using Obsidian.Plugins;
using System.Threading;

namespace Obsidian.Entities;

// Movement abilities mods grant (extra jumps, gliding without an elytra, flight), honoured here and, through
// MovementAbilitiesPayload and vanilla's abilities packet, by the player's client. The server doesn't simulate
// players: like vanilla, it takes their positions, so these only keep its own state (gliding, fall distance, flight)
// in step with what the client does.
public partial class Player
{
    private readonly Lock movementGate = new();

    // The grants by id, with the plugin that made each, whose unloading takes it back; null until there's one.
    private Dictionary<string, (MovementAbilities Abilities, PluginContainer? Owner)>? movementGrants;

    // The abilities last sent to the client's Obsidian channel, null until sent while it's in play.
    private MovementAbilities? movementAbilitiesSent;

    // Mid-air jumps used since the player last stood on the ground.
    private int extraJumpsUsed;

    public MovementAbilities MovementAbilities { get; private set; }

    public void SetMovementAbilities(string id, MovementAbilities abilities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var owner = PluginEntities.FindSpawner();
        var grant = abilities with { ExtraJumps = Math.Max(0, abilities.ExtraJumps) };

        lock (this.movementGate)
        {
            owner?.ThrowIfClosing();
            this.movementGrants ??= [];
            this.movementGrants[id] = (grant, owner);
        }

        this.ApplyMovementAbilities();
    }

    public bool RemoveMovementAbilities(string id)
    {
        lock (this.movementGate)
        {
            if (this.movementGrants?.Remove(id) != true)
                return false;
        }

        this.ApplyMovementAbilities();
        return true;
    }

    /// <summary>
    /// Takes back the grants <paramref name="plugin"/> made, as it unloads.
    /// </summary>
    internal void ReleaseMovementAbilities(PluginContainer plugin)
    {
        lock (this.movementGate)
        {
            if (this.movementGrants is null)
                return;

            var owned = this.movementGrants
                .Where(grant => grant.Value.Owner == plugin)
                .Select(grant => grant.Key)
                .ToList();

            if (owned.Count == 0)
                return;

            foreach (var id in owned)
                this.movementGrants.Remove(id);
        }

        this.ApplyMovementAbilities();
    }

    /// <summary>
    /// The flight abilities granted on top of the game mode's: vanilla's <c>mayfly</c> for <c>CanFly</c>.
    /// </summary>
    private PlayerAbility GrantedFlight =>
        this.MovementAbilities.CanFly ? PlayerAbility.AllowFlying : PlayerAbility.None;

    // Combines the grants, and lets the player fly, or stops it flying, as flight was granted or taken back. The other
    // abilities reach the client on the next level tick (SynchronizeMovementAbilities).
    private void ApplyMovementAbilities()
    {
        lock (this.movementGate)
        {
            this.MovementAbilities = this.movementGrants?.Values
                .Aggregate(default(MovementAbilities), (total, grant) => total.Combine(grant.Abilities))
                ?? default;
        }

        var mayFly = this.GameMode is GameMode.Creative or GameMode.Spectator || this.MovementAbilities.CanFly;
        var abilities = mayFly
            ? this.Abilities | PlayerAbility.AllowFlying
            : this.Abilities & ~(PlayerAbility.AllowFlying | PlayerAbility.Flying);

        if (abilities == this.Abilities)
            return;

        this.Abilities = abilities;
        this.SendPacket(new PlayerAbilitiesPacket { Abilities = abilities });
    }

    /// <summary>
    /// Sends the player's Obsidian client its movement abilities when they changed, on the level tick.
    /// </summary>
    internal void SynchronizeMovementAbilities()
    {
        // A client back in configuration (a reload, say) is sent them again once it plays.
        if (this.Client.State != ClientState.Play)
        {
            this.movementAbilitiesSent = null;
            return;
        }

        var abilities = this.MovementAbilities;
        if (this.movementAbilitiesSent == abilities)
            return;

        if (!this.Client.HasChannel(MovementAbilitiesPayload.Channel))
            return;

        var packet = CustomPayloadPacket.ClientboundPlay with
        {
            Channel = MovementAbilitiesPayload.Channel,
            PluginData = MovementAbilitiesPayload.Encode(abilities)
        };

        if (this.Client.SendPacket(packet))
            this.movementAbilitiesSent = abilities;
    }

    /// <summary>
    /// Counts a press of jump in the air as an extra jump, while the player has one left, forgiving its fall so far,
    /// as the Obsidian client jumps again on that press (see <see cref="MovementAbilities.ExtraJumps"/>).
    /// </summary>
    internal void TrackJumpInput(PlayerInput previous, PlayerInput current)
    {
        if (!current.HasFlag(PlayerInput.Jump) || previous.HasFlag(PlayerInput.Jump))
            return;

        if (this.extraJumpsUsed >= this.MovementAbilities.ExtraJumps || !this.MidAir())
            return;

        this.extraJumpsUsed++;
        this.FallDistance = 0;
    }

    /// <summary>
    /// Starts gliding at the client's request, like vanilla's <c>tryToStartFallFlying</c>: in the air, out of fluids,
    /// with a glider on the chest or the <c>CanGlide</c> ability.
    /// </summary>
    internal void TryStartGliding()
    {
        if (this.FlyingWithElytra || !this.MidAir() || !this.CanGlide())
            return;

        this.FlyingWithElytra = true;
        this.BroadcastFlags();
    }

    /// <summary>
    /// Keeps the abilities' state in step with a movement the client reported: a terrain-supported landing gives back
    /// extra jumps. Like vanilla's <c>updateFallFlying</c>, landing ends gliding, as does mounting, flying or losing
    /// the glider.
    /// </summary>
    private void TrackMovementAbilities(MovementFlags flags, bool supported)
    {
        var onGround = flags.HasFlag(MovementFlags.OnGround);
        if (supported)
            this.extraJumpsUsed = 0;

        if (!this.FlyingWithElytra)
            return;

        if (!onGround && this.Vehicle is null && !this.Abilities.HasFlag(PlayerAbility.Flying) && this.CanGlide())
            return;

        this.FlyingWithElytra = false;
        this.BroadcastFlags();
    }

    // Whether the player is free in the air: off the ground, not riding, flying or gliding, and out of fluids and
    // climbable blocks, where jump swims or climbs instead.
    private bool MidAir()
    {
        if (this.MovementFlags.HasFlag(MovementFlags.OnGround)
            || this.Vehicle is not null
            || this.Abilities.HasFlag(PlayerAbility.Flying)
            || this.FlyingWithElytra)
        {
            return false;
        }

        var feet = new AI.MobTerrain(this.Level).GetBlock((Vector)this.Position.Floor());

        return feet is null
            || feet.RegistryId is not (BlockIds.Water or BlockIds.Lava or BlockIds.BubbleColumn)
                && !TagsRegistry.Block.Climbable.Entries.Contains(feet.RegistryId);
    }

    // Vanilla's canGlide: no levitation, and a glider on the chest that isn't about to break, or the ability.
    private bool CanGlide()
    {
        if (this.HasPotionEffect((int)PotionEffect.Levitation - 1))
            return false;

        if (this.MovementAbilities.CanGlide)
            return true;

        var chest = this.Inventory.GetItem(6);

        return chest is not null
            && (chest.Is(ItemsRegistry.Elytra) || chest.ContainsKey(DataComponentType.Glider))
            && (chest.MaxDamage == 0 || chest.Damage < chest.MaxDamage - 1);
    }

    // The shared flags (gliding among them) to the player's watchers and the player itself, as vanilla sends a
    // player's own entity data.
    private void BroadcastFlags() => this.PacketBroadcaster.QueuePacketToLevel(this.Level, new SetEntityDataPacket
    {
        EntityId = this.EntityId,
        Entity = this
    });
}
