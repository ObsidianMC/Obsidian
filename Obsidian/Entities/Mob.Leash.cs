using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Entities;

// A lead from a mob to the entity holding it, as far as saved worlds and travel need: vanilla's "leash" field, the
// link packet, and the lead coming off when its holder is gone or never turns up in the mob's level. Using leads on
// mobs, fence knots and the lead's pull aren't modelled; a lead tied to a fence keeps its saved field untouched.
public partial class Mob
{
    // Vanilla's Leashable.restoreLeashFromSave: a mob that has ticked more than 100 times since it was loaded or
    // arrived from another level without finding its holder drops the lead.
    private const int LeashRestoreTicks = 100;

    private Entity? leashHolder;
    private Guid? pendingLeashHolder;
    private int leashRestoreTicks;

    /// <summary>The entity holding the mob's lead, once it's found in the mob's level.</summary>
    internal Entity? LeashHolder => this.leashHolder;

    /// <summary>Ties the mob's lead to <paramref name="holder"/> and shows it to players tracking the mob.</summary>
    internal void LeashTo(Entity holder)
    {
        this.leashHolder = holder;
        this.pendingLeashHolder = null;
        this.SynchronizeLeash();
    }

    /// <summary>Takes the lead off, dropping a lead item when <paramref name="dropItem"/> is set.</summary>
    internal void DropLeash(bool dropItem)
    {
        if (this.leashHolder is null && this.pendingLeashHolder is null)
            return;

        var shown = this.leashHolder is not null;
        this.leashHolder = null;
        this.pendingLeashHolder = null;
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
        if (!this.IsActive || !holder.IsActive || holder is not PlayerBase && holder.Level != this.Level)
            this.DropLeash(this.Level.LevelData.GetBooleanRule("entity_drops"));
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
        var holder = this.leashHolder?.Uuid ?? this.pendingLeashHolder;
        if (holder is Guid uuid)
        {
            tag.Set(new NbtCompound("leash") { new NbtArray<int>("UUID", EntityNbt.UuidToInts(uuid)) });
            return;
        }

        if (tag.TryGetTag<NbtCompound>("leash", out _))
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
    }
}
