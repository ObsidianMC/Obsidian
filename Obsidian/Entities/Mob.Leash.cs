using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Entities;

// A lead from a mob to the entity holding it, like vanilla's Leashable: players lead mobs with leads and tie them to
// fences (see LeashKnot), the lead pulls a mob that strays and breaks when it strays too far, and it comes off when
// its holder is gone or never turns up in the mob's level. Saved worlds keep vanilla's "leash" field.
public partial class Mob
{
    // Vanilla's lead lengths: past 6 blocks the lead pulls, past 10 it breaks.
    private const double LeashPullDistance = 6;
    private const double LeashBreakDistance = 10;

    // The fence a mob loaded tied to one is tied to again once it ticks.
    private Vector? pendingKnot;

    // Vanilla's Leashable.restoreLeashFromSave: a mob that has ticked more than 100 times since it was loaded or
    // arrived from another level without finding its holder drops the lead.
    private const int LeashRestoreTicks = 100;

    private Entity? leashHolder;
    private Guid? pendingLeashHolder;
    private int leashRestoreTicks;

    /// <summary>The entity holding the mob's lead, once it's found in the mob's level.</summary>
    internal Entity? LeashHolder => this.leashHolder;

    /// <summary>
    /// Whether a lead can be put on the mob, like vanilla's <c>canBeLeashed</c>: not on monsters, save hoglins and
    /// zoglins, nor on villagers and traders, nor on a mob already led.
    /// </summary>
    internal virtual bool CanBeLeashed =>
        this.leashHolder is null && this.Alive && this is not (Villager or WanderingTrader)
        && (this is Hoglin or Zoglin || MobSpawner.SpawnCategory(this) != "monster");

    /// <summary>
    /// A player's use of the mob, as far as leads go, like vanilla's <c>Mob.checkAndHandleImportantInteractions</c>:
    /// using a mob the player leads lets go of it, and using a lead on one that can be leashed leads it.
    /// </summary>
    /// <returns>Whether the lead used the interaction.</returns>
    internal async ValueTask<bool> UseLeashAsync(PlayerBase player, InteractionHand hand)
    {
        if (player.GameMode == GameMode.Spectator)
            return false;

        if (ReferenceEquals(this.leashHolder, player))
        {
            this.DropLeash(player.GameMode != GameMode.Creative);
            this.PlayLeadSound("minecraft:item.lead.untied");
            return true;
        }

        var slot = hand == InteractionHand.OffHand ? 45 : player.CurrentHeldItemSlot;
        if (player.Inventory.GetItem(slot) is not { Count: > 0 } lead || !lead.Is(ItemsRegistry.Lead) || !this.CanBeLeashed)
            return false;

        this.LeashTo(player);
        this.PlayLeadSound("minecraft:item.lead.tied");

        if (player.GameMode != GameMode.Creative)
        {
            player.Inventory.RemoveItem(slot, 1);
            await player.SyncInventorySlotAsync(slot);
        }

        return true;
    }

    private void PlayLeadSound(string sound) =>
        this.PacketBroadcaster.QueuePacketToLevelInRange(this.Level, this.Position, new SoundPacket
        {
            SoundLocation = sound,
            SoundPosition = new SoundPosition(this.Position.X, this.Position.Y, this.Position.Z),
            Category = SoundCategory.Neutral,
            Volume = 1,
            Pitch = 1,
            Seed = Random.Shared.NextInt64()
        });

    /// <summary>Ties the mob's lead to <paramref name="holder"/> and shows it to players tracking the mob.</summary>
    internal void LeashTo(Entity holder)
    {
        this.leashHolder = holder;
        this.pendingLeashHolder = null;
        this.pendingKnot = null;
        this.SynchronizeLeash();
    }

    /// <summary>Takes the lead off, dropping a lead item when <paramref name="dropItem"/> is set.</summary>
    internal void DropLeash(bool dropItem)
    {
        if (this.leashHolder is null && this.pendingLeashHolder is null && this.pendingKnot is null)
            return;

        var shown = this.leashHolder is not null;
        this.leashHolder = null;
        this.pendingLeashHolder = null;
        this.pendingKnot = null;
        if (dropItem)
            this.DropItem(ItemsRegistry.Lead);

        if (shown)
            this.SynchronizeLeash();
    }

    /// <summary>
    /// Keeps only the holder's UUID when the mob arrives in another level, like vanilla's copy of a travelling entity:
    /// the holder is looked up again there, and the lead drops if it doesn't turn up in time.
    /// </summary>
    internal void HoldLeashByUuid()
    {
        if (this.leashHolder is not Entity holder)
            return;

        this.leashHolder = null;
        this.pendingLeashHolder = holder.Uuid;
        this.leashRestoreTicks = 0;
    }

    /// <summary>Tells a player that just started tracking the mob where its lead goes.</summary>
    internal ValueTask SendLeashToAsync(Player player) => this.leashHolder is Entity holder
        ? player.Client.QueuePacketAsync(this.CreateLeashPacket(holder))
        : default;

    internal void TickLeash()
    {
        // A mob loaded tied to a fence ties itself to a knot there, as long as the fence still stands.
        if (this.pendingKnot is Vector fence && this.Level is AbstractLevel knotLevel)
        {
            this.pendingKnot = null;
            if (LeashKnot.IsFence(knotLevel.GetLoadedBlock(fence)) && LeashKnot.GetOrCreate(knotLevel, fence) is LeashKnot knot)
                this.LeashTo(knot);
            else
                this.DropItem(ItemsRegistry.Lead);

            return;
        }

        if (this.pendingLeashHolder is Guid uuid)
        {
            if (this.FindInLevel(uuid) is Entity found)
            {
                this.LeashTo(found);
            }
            else if (++this.leashRestoreTicks > LeashRestoreTicks)
            {
                // Unlike a broken lead, vanilla drops this one regardless of the entity_drops rule.
                this.pendingLeashHolder = null;
                this.DropItem(ItemsRegistry.Lead);
            }

            return;
        }

        if (this.leashHolder is not Entity holder)
            return;

        // Vanilla breaks the lead when either end can't interact with the world. A holder that travelled was replaced
        // by a copy in vanilla, so only a player holder keeps its lead from another level.
        if (!this.IsActive || !holder.IsActive || holder is LeashKnot { Removed: true }
            || holder is not IPlayer && holder.Level != this.Level)
        {
            this.DropLeash(this.Level.LevelData.GetBooleanRule("entity_drops"));
            return;
        }

        if (holder.Level != this.Level)
            return;

        // Vanilla's elastic lead: it breaks when stretched too far, and pulls the mob back when it strays.
        var offset = holder.Position - this.Position;
        var distance = offset.Magnitude;

        if (distance > LeashBreakDistance)
        {
            this.DropLeash(this.Level.LevelData.GetBooleanRule("entity_drops"));
            this.PlayLeadSound("minecraft:item.lead.break");
            return;
        }

        if (distance > LeashPullDistance)
        {
            var direction = offset / distance;
            this.Motion += new VectorD(
                Math.CopySign(direction.X * direction.X * 0.4, direction.X),
                Math.CopySign(direction.Y * direction.Y * 0.4, direction.Y),
                Math.CopySign(direction.Z * direction.Z * 0.4, direction.Z));
        }
    }

    private Entity? FindInLevel(Guid uuid)
    {
        if (this.Level.Players.TryGetValue(uuid, out var player))
            return player as Entity;

        if (this.Level is not AbstractLevel level)
            return null;

        foreach (var region in level.Regions.Values)
        {
            foreach (var entity in region.Entities.Values)
            {
                if (entity.Uuid == uuid && entity is Entity concrete && !ReferenceEquals(concrete, this))
                    return concrete;
            }
        }

        return null;
    }

    private void SynchronizeLeash() => this.PacketBroadcaster.QueuePacketToLevelInRange(
        this.Level,
        this.Position,
        this.CreateLeashPacket(this.leashHolder));

    private SetEntityLinkPacket CreateLeashPacket(Entity? holder) => new()
    {
        LeashedId = this.EntityId,
        HolderId = holder?.EntityId ?? 0
    };

    // Vanilla saves a lead held by an entity as {UUID: [I; ...]} and one tied to a fence as the knot's position.
    private void WriteLeash(NbtCompound tag)
    {
        // A lead tied to a fence is saved as the fence's position, as vanilla saves it.
        if (((this.leashHolder as LeashKnot)?.Fence ?? this.pendingKnot) is Vector fence)
        {
            tag.Set(new NbtArray<int>("leash", [fence.X, fence.Y, fence.Z]));
            return;
        }

        var holder = this.leashHolder?.Uuid ?? this.pendingLeashHolder;
        if (holder is Guid uuid)
        {
            tag.Set(new NbtCompound("leash") { new NbtArray<int>("UUID", EntityNbt.UuidToInts(uuid)) });
            return;
        }

        if (tag.TryGetTag<INbtTag>("leash", out _))
            tag.Remove("leash");
    }

    private void ReadLeash(NbtCompound tag)
    {
        if (tag.TryGetTag<NbtCompound>("leash", out var leash)
            && leash.TryGetTag<NbtArray<int>>("UUID", out var holder)
            && holder.Count == 4)
        {
            this.pendingLeashHolder = EntityNbt.UuidFromInts(holder.GetArray());
            this.leashRestoreTicks = 0;
        }
        else if (tag.TryGetTag<NbtArray<int>>("leash", out var fence) && fence.Count == 3)
        {
            var position = fence.GetArray();
            this.pendingKnot = new Vector(position[0], position[1], position[2]);
        }
    }
}
