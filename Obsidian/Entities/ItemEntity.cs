using Obsidian.API.Inventory;
using Obsidian.Nbt;
using Obsidian.Entities.AI;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:item")]
public partial class ItemEntity : Entity
{
    private int age;

    // Ticks since it was spawned or loaded, like vanilla's unsaved Entity.tickCount; it paces merging.
    private int ticksExisted;

    private int? restPhase;

    // Whether it touched water when it last checked, like vanilla's wasTouchingWater, which decides its splash.
    private bool wasTouchingWater;

    internal override bool TouchesWaterForFalls => this.wasTouchingWater;
    internal void SetExtendedLifetime() => age = -6000;
    // Merges, pickups, hopper pulls and removal check and set it under the transfer lock of the level owning the item
    // (see Entity.EnterOwningLevel), so a stack isn't transferred after its entity is removed, even when the removal
    // comes from outside the level tick.
    internal bool Removed { get; set; }
    private int pickupDelay = 10;

    // Kept across ticks, as mobs keep theirs, rather than made (with its chunk cache) every tick; made again when the
    // entity changes level.
    private MobTerrain? terrain;
    private ILevel? terrainLevel;

    public ItemStack Item { get; set; }

    /// <summary>
    /// What, with its age, picks the ticks the item moves on while it rests on the ground (vanilla's entity id, which
    /// staggers resting items): its own id unless set. Replays set vanilla's, as ids can't match between servers.
    /// </summary>
    internal int RestPhase
    {
        get => this.restPhase ?? this.EntityId;
        set => this.restPhase = value;
    }

    public bool CanPickup
    {
        get => this.pickupDelay == 0;
        set => this.pickupDelay = value ? 0 : 10;
    }

    public DateTimeOffset TimeDropped { get; private set; } = DateTimeOffset.UtcNow;

    // Like vanilla's, its box follows its position, which spawning and loading set without moving it: an item that
    // lies still from the start must still be found by hoppers, hopper minecarts and pressure plates.
    public override BoundingBox BoundingBox => this.Dimension.CreateBBFromPosition(this.Position);

    public ItemEntity() => this.Type = EntityType.Item;

    public override void SpawnEntity(Velocity? velocity = null, int additionalData = 0)
    {
        if (velocity is { } initial)
            Motion = new VectorD(initial.X, initial.Y, initial.Z);
        base.SpawnEntity(velocity, additionalData);
    }

    internal static bool Drop(IPlayer player, ItemStack stack) => Drop(player, stack, hostOwned: false);

    /// <summary>
    /// Drops a stack in front of the player like <see cref="Drop(IPlayer, ItemStack)"/>, without counting it as
    /// dropped: vanilla's <c>Player.drop</c> for an item a use gave back that didn't fit their inventory. The server
    /// owns the drop, so a level that can't take it yet gets it on a later entity action rather than losing it.
    /// </summary>
    internal static void DropUncounted(IPlayer player, ItemStack stack)
    {
        // Player.drop uses a float eye height and float 0.3 offset, promoted to double for the position.
        var eyeHeight = (float)(player.HeadY - player.Position.Y);
        var dropY = player.Position.Y + eyeHeight - 0.3f;
        Drop(player, stack, hostOwned: true, counted: false, dropY: dropY);
    }

    // Finished menu clicks drop the player's items, regardless of the menu's ambient plugin call.
    internal static void DropHostOwned(IPlayer player, ItemStack stack) => Drop(player, stack, hostOwned: true);

    /// <summary>
    /// Drops a stack around the player, like vanilla's <c>Player.drop(stack, dropAround: true)</c> as a death drops
    /// their items: pushed up to half a block a tick in a random direction, and 0.2 up. It counts no statistics.
    /// </summary>
    internal static void DropAround(IPlayer player, ItemStack stack)
        => DropAround(player.Level, player.Position, player.HeadY - 0.3, stack);

    // A death can be deferred past a teleport: its drop belongs to the captured level and position.
    internal static void DropAround(ILevel level, VectorD position, double dropY, ItemStack stack)
    {
        var power = Globals.Random.NextSingle() * 0.5f;
        var angle = Globals.Random.NextSingle() * MathF.Tau;

        var item = new ItemEntity
        {
            EntityId = Server.GetNextEntityId(),
            Item = new ItemStack(stack, stack.Count),
            Level = level,
            Position = new VectorD(position.X, dropY, position.Z),
            Motion = new VectorD(-MathF.Sin(angle) * power, 0.2, MathF.Cos(angle) * power),
            pickupDelay = 40
        };
        PublishDrop(item);
    }

    private static void PublishDrop(ItemEntity item)
    {
        var added = item.Level is AbstractLevel level
            ? level.TryAddEntity(item, hostOwned: true)
            : item.Level.TryAddEntity(item);

        if (added)
        {
            item.SpawnEntity(new Velocity(item.Motion.X, item.Motion.Y, item.Motion.Z));
        }
        else if (item.Level is AbstractLevel pending)
        {
            pending.EnqueueEntityAction(() =>
            {
                PublishDrop(item);
                return ValueTask.CompletedTask;
            });
        }
    }

    // Thrown where the player looks, or with the given motion for a drop around them.
    private static bool Drop(IPlayer player, ItemStack stack, bool hostOwned, VectorD? around = null,
        bool counted = true, double? dropY = null)
    {
        var direction = player.GetLookDirection();
        var motion = around ?? new VectorD(direction.X * 0.3, direction.Y * 0.3 + 0.1, direction.Z * 0.3);
        var item = new ItemEntity
        {
            EntityId = Server.GetNextEntityId(),
            Item = new ItemStack(stack, stack.Count),
            Level = player.Level,
            Position = new VectorD(player.Position.X, dropY ?? player.HeadY - 0.3, player.Position.Z),
            Motion = motion,
            // Vanilla gives an item a player drops 40 ticks before anyone can pick it up, so the thrower doesn't
            // collect it again straight away.
            pickupDelay = 40
        };
        var added = player.Level is AbstractLevel level
            ? level.TryAddEntity(item, hostOwned)
            : player.Level.TryAddEntity(item);

        if (!added)
        {
            if (hostOwned && player.Level is AbstractLevel pending)
                pending.EnqueueEntityAction(RetryDrop);

            return false;
        }

        if (around is null && counted)
        {
            player.Statistics.Increment(stack.Holder.UnlocalizedName, stack.Count, "minecraft:dropped");
            player.Statistics.Increment("minecraft:drop");
        }

        item.SpawnEntity(new Velocity(motion.X, motion.Y, motion.Z));
        return true;

        ValueTask RetryDrop()
        {
            if (item.Level is AbstractLevel retry)
            {
                if (retry.TryAddEntity(item, hostOwned: true))
                    item.SpawnEntity(new Velocity(item.Motion.X, item.Motion.Y, item.Motion.Z));
                else
                    retry.EnqueueEntityAction(RetryDrop);
            }

            return ValueTask.CompletedTask;
        }
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteEntityMetadataType(8, EntityMetadataType.Slot);
        writer.WriteItemStack(this.Item);
    }

    // Vanilla's default pickup delay is 10 ticks; a stack that can't be picked up yet keeps it.
    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);

        tag.Set(this.Item.ToNbt("Item"));
        tag.Set(new NbtTag<short>("Age", (short)Math.Clamp(age, short.MinValue, short.MaxValue)));
        tag.Set(new NbtTag<short>("PickupDelay", (short)this.pickupDelay));
    }

    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);

        if (tag.TryGetTag<NbtCompound>("Item", out var item) && item.ItemFromNbt() is ItemStack stack)
            this.Item = stack;

        if (tag.TryGetNumber<short>("Age", out var savedAge)) age = savedAge;
        this.pickupDelay = tag.TryGetNumber<short>("PickupDelay", out var delay) ? Math.Max(0, (int)delay) : 10;
    }

    public async override ValueTask TickAsync()
    {
        await base.TickAsync();
        this.ticksExisted++;
        using (this.EnterOwningLevel(out _))
        {
            if (this.Removed)
                return;
            if (this.pickupDelay > 0)
                this.pickupDelay--;
        }
        if (++age >= 6000)
        {
            await RemoveAsync();
            return;
        }
        if (this.terrain is null || !ReferenceEquals(this.terrainLevel, this.Level))
        {
            this.terrain = new MobTerrain(this.Level);
            this.terrainLevel = this.Level;
        }

        var from = Position;
        this.UpdateFluidContact(firstTick: this.ticksExisted == 1);
        var next = EntityMovement.MoveItem(this, this.terrain, this.ticksExisted);
        if (Level is AbstractLevel level && !level.TryMoveEntity(this, Position, next))
            return;
        if (next != Position)
            await UpdateAsync(next, MovementFlags);

        // Vanilla's item updates its fluid contact again once it has moved, after its first tick.
        this.UpdateFluidContact(firstTick: false);

        // Like vanilla's ItemEntity.tick, it looks for items to merge with every 2 ticks after moving into another
        // block and every 40 otherwise, which decides how soon dropped items stack up.
        var movedBlock = (Vector)from.Floor() != (Vector)Position.Floor();
        if (this.ticksExisted % (movedBlock ? 2 : 40) != 0)
            return;

        await this.MergeWithNeighboursAsync();
    }

    /// <summary>
    /// Vanilla's <c>Entity.updateFluidInteraction</c>, which an item runs as its tick starts and again once it has
    /// moved: in water its fall is forgotten and, coming in from out of it (not on its first tick), it splashes; then
    /// the water's current and the lava's push it.
    /// </summary>
    private void UpdateFluidContact(bool firstTick)
    {
        if (this.terrain is not MobTerrain terrain || this.Level is not AbstractLevel level)
            return;

        var bounds = this.Dimension.CreateBBFromPosition(this.Position);
        var inWater = terrain.FluidHeight(bounds, water: true) is not null;
        var inLava = terrain.FluidHeight(bounds, water: false) is not null;
        if (inWater)
        {
            this.ResetFallDistance();
            if (!this.wasTouchingWater && !firstTick)
                this.Splash(level);
        }

        this.wasTouchingWater = inWater;
        if (inWater)
            this.Motion += terrain.FluidPush(bounds, water: true, this.Motion);

        if (inLava)
            this.Motion += terrain.FluidPush(bounds, water: false, this.Motion);
    }

    // Vanilla's doWaterSplashEffect for an item: a splash as loud as it was moving (sideways counting a fifth), its
    // pitch from the item's own random, and the splash game event. Its bubbles are drawn by clients.
    private void Splash(AbstractLevel level)
    {
        var motion = this.Motion;
        var moving = Math.Sqrt(motion.X * motion.X * 0.2f + motion.Y * motion.Y + motion.Z * motion.Z * 0.2f);
        var volume = Math.Min(1f, (float)moving * 0.2f);
        var pitch = 1f + (Globals.Random.NextSingle() - Globals.Random.NextSingle()) * 0.4f;
        // Vanilla's Entity.playSound skips a silent entity's sound, after the pitch's draws and before the event.
        if (!this.Silent)
            LevelSounds.Play(level, this.Position, "minecraft:entity.generic.splash", SoundCategory.Neutral, volume, pitch);
        level.EmitGameEvent(MobGameEvent.Splash, this.Position, this);
    }

    // Vanilla's mergeWithNeighbours: each item whose box meets this one's, widened by half a block across, that can
    // take the other's stack whole. The smaller stack goes into the larger, and on a tie this one into the other,
    // which decides which of them is left and where.
    private async ValueTask MergeWithNeighboursAsync()
    {
        var reach = this.BoundingBox;
        reach = new BoundingBox(reach.Min - new VectorD(0.5, 0, 0.5), reach.Max + new VectorD(0.5, 0, 0.5));
        foreach (var ent in this.Level.GetNonPlayerEntitiesInRange(this.Position, 1.5f))
        {
            if (ent is not ItemEntity itemEntity || ReferenceEquals(itemEntity, this)
                || !MobTerrain.Overlaps(reach, itemEntity.BoundingBox))
            {
                continue;
            }

            ItemEntity? emptied;
            using (this.EnterOwningLevel(out var owner))
            {
                // Both stacks are this level's only while the other item hasn't left it since the lookup.
                if (itemEntity.Level != owner || !itemEntity.IsMemberOf(owner) || !this.IsMemberOf(owner))
                    continue;
                if (!this.IsMergeable || !itemEntity.IsMergeable || this.Item != itemEntity.Item
                    || this.Item.Count + itemEntity.Item.Count > itemEntity.Item.MaxStackSize)
                {
                    continue;
                }

                var (into, merged) = itemEntity.Item.Count < this.Item.Count ? (this, itemEntity) : (itemEntity, this);
                var count = Math.Min(Math.Min(into.Item.MaxStackSize, 64) - into.Item.Count, merged.Item.Count);
                into.Item.Count += count;
                merged.Item.Count -= count;
                into.pickupDelay = Math.Max(into.pickupDelay, merged.pickupDelay);
                into.age = Math.Min(into.age, merged.age);
                emptied = merged.Item.Count == 0 ? merged : null;
                if (emptied is not null)
                    emptied.Removed = true;

                into.SendItemUpdate();
                if (emptied is null)
                    merged.SendItemUpdate();
            }

            if (emptied is null)
                continue;

            await emptied.RemoveAsync();
            if (ReferenceEquals(emptied, this))
                break;
        }
    }

    // Vanilla's isMergable: still there, not past its lifetime, and with room in its stack.
    private bool IsMergeable =>
        !this.Removed && this.age < 6000 && !this.Item.IsNullOrAir() && this.Item.Count < this.Item.MaxStackSize;

    /// <summary>What <see cref="TryTake"/> took from an item entity's stack, and whether that emptied it.</summary>
    internal readonly record struct Taken(ItemStack Stack, bool Emptied);

    /// <summary>
    /// Takes up to <paramref name="count"/> of the stack for something in <paramref name="level"/> that found the item
    /// there, such as a mob picking it up, the way a player's pickup takes it: under the transfer lock of the level that
    /// owns the item, only while that's still <paramref name="level"/> and the item isn't removed, can be picked up and
    /// holds a stack <paramref name="accepts"/> takes. An item it empties is marked removed at once, so nothing else can
    /// take it; the caller removes it (see <see cref="RemoveAsync"/>) once it has told the clients.
    /// </summary>
    /// <returns>What was taken, or null when nothing was.</returns>
    internal Taken? TryTake(ILevel level, int count, Func<ItemStack, bool> accepts)
    {
        using (this.EnterOwningLevel(out var owner))
        {
            // A removed or unloaded item keeps its level, so it's checked to still be one of the level's entities.
            if (owner != level || this.Removed || !this.CanPickup || !this.IsMemberOf(level))
                return null;

            if (this.Item is not { IsAir: false, Count: > 0 } stack || !accepts(stack))
                return null;

            var amount = Math.Min(count, stack.Count);
            if (amount <= 0)
                return null;

            var taken = new Taken(new ItemStack(stack, amount), amount == stack.Count);
            stack.Count -= amount;
            this.Removed = taken.Emptied;
            return taken;
        }
    }

    internal void SendItemUpdate() => this.PacketBroadcaster.QueuePacketToLevel(this.Level,
        new SetEntityDataPacket { EntityId = this.EntityId, Entity = this });

    // Vanilla's ItemEntity health: what's left of it before damage destroys it.
    private float itemHealth = 5;

    /// <summary>Cactus takes one of the item's five health on each contact, including fire-resistant items.</summary>
    internal async ValueTask HurtByCactusAsync()
    {
        bool destroyed;
        using (this.EnterOwningLevel(out var level))
        {
            if (this.Removed || !this.IsMemberOf(level))
                return;

            this.itemHealth--;
            destroyed = this.itemHealth <= 0;
            this.Removed = destroyed;
            if (level is AbstractLevel world)
                world.EmitGameEvent(MobGameEvent.EntityDamage, this.Position);
        }

        if (destroyed)
            await this.RemoveAsync();
    }

    /// <summary>
    /// Hurts it by <paramref name="damage"/> from an explosion, as vanilla's <c>ItemEntity.hurtServer</c> does: a nether
    /// star withstands it, and anything else is gone once its five health is.
    /// </summary>
    /// <returns>Whether it was destroyed.</returns>
    internal ValueTask<bool> HurtByExplosionAsync(float damage)
    {
        if (this.Item.Holder.Id == ItemIds.NetherStar)
            return ValueTask.FromResult(false);

        return this.HurtAsync(damage);
    }

    // Vanilla's Entity.thunderHit on an item: five lightning damage, which nothing withstands (fire resistant items
    // only withstand fire, and lightning isn't fire), so it's gone.
    internal override async ValueTask ThunderHitAsync(LightningBolt bolt) => await this.HurtAsync(5);

    // Vanilla's ItemEntity.hurtServer once the item takes the damage: the damage's game event, which names no
    // source for explosions and lightning, then the item is gone once its five health is.
    private async ValueTask<bool> HurtAsync(float damage)
    {
        this.itemHealth -= damage;
        if (this.Level is WorldData.AbstractLevel level)
            level.EmitGameEvent(WorldData.MobGameEvent.EntityDamage, this.Position);

        if (this.itemHealth > 0)
            return false;

        await this.RemoveAsync();
        return true;
    }

    public override async ValueTask RemoveAsync()
    {
        using (this.EnterOwningLevel(out _))
            this.Removed = true;
        await base.RemoveAsync();
    }

}
