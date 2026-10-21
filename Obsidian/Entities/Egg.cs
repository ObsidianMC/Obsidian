using Obsidian.API.Inventory;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Entities;

/// <summary>
/// A thrown egg, like vanilla's <c>ThrownEgg</c>: it flies with gravity until it hits a block or a living entity,
/// then cracks. One time in eight it hatches a baby chicken, and one time in 32 of those, four.
/// </summary>
/// <remarks>
/// The throw itself is vanilla's <c>EggItem.use</c>, reached through <see cref="ItemUseBehaviors"/>.
/// </remarks>
[MinecraftEntity("minecraft:egg")]
public sealed partial class Egg : Throwable
{
    // Vanilla's ThrowableProjectile gravity, which eggs and snowballs share.
    private const double Gravity = 0.03;

    // Vanilla's ThrowableItemProjectile.shootFromRotation speed for eggs.
    private const float Speed = 1.5f;

    private int age;

    public Egg() => this.Type = EntityType.Egg;

    internal IEntity? Owner { get; set; }

    /// <summary>The egg item clients show.</summary>
    public ItemStack Item { get; internal set; } = new(ItemsRegistry.Egg, 1);

    /// <summary>
    /// Vanilla's <c>EggItem.use</c>: throws an egg from <paramref name="player"/>, using one up unless the player is
    /// in creative mode.
    /// </summary>
    internal static async ValueTask ThrowAsync(PlayerBase player, InteractionHand hand, ItemStack stack)
    {
        if (player.Level is not AbstractLevel level)
            return;

        var egg = new Egg
        {
            Level = level,
            EntityId = Server.GetNextEntityId(),
            Owner = player,
            Position = new VectorD(player.Position.X, player.HeadY - 0.1, player.Position.Z),
            Motion = player.ShotDirection(0) * Speed + player.CombatMotion,
            Item = new ItemStack(stack, 1)
        };
        egg.BoundingBox = egg.Dimension.CreateBBFromPosition(egg.Position);

        if (!level.TrySpawnEntity(egg))
            return;

        level.PacketBroadcaster.BroadcastToLevelInRange(level, (Vector)player.Position.Floor(), new SoundPacket
        {
            SoundLocation = "minecraft:entity.egg.throw",
            SoundPosition = new SoundPosition(player.Position.X, player.Position.Y, player.Position.Z),
            Category = SoundCategory.Neutral,
            Volume = 0.5f,
            Pitch = 0.4f / (Globals.Random.NextSingle() * 0.4f + 0.8f),
            Seed = Globals.Random.NextInt64()
        });

        await ThrowableItemUse.ConsumeAsync(player, hand, stack);
    }

    public override void SpawnEntity(Velocity? velocity = null, int additionalData = 0)
    {
        base.SpawnEntity(velocity ?? new Velocity(this.Motion.X, this.Motion.Y, this.Motion.Z), this.Owner?.EntityId ?? additionalData);
        if (this.Level is AbstractLevel level)
            level.EmitGameEvent(MobGameEvent.ProjectileShoot, this.Position, this.Owner ?? this);
    }

    public override async ValueTask TickAsync()
    {
        if (this.Level is not AbstractLevel level || !level.IsMobTicking(this.Position))
            return;

        if (++this.age > 1200 || this.Position.Y < -128)
        {
            await this.RemoveAsync();
            return;
        }

        var terrain = new MobTerrain(level);
        var end = this.Position + this.Motion;
        var swept = new BoundingBox(
            VectorD.Min(this.Position, end) - new VectorD(0.3f),
            VectorD.Max(this.Position, end) + new VectorD(0.3f));
        var fraction = 1d;
        IEntity? target = null;

        foreach (var shape in terrain.GetCollisions(swept))
        {
            if (MobTerrain.RayIntersection(shape, this.Position, this.Motion) is double hit)
                fraction = Math.Min(fraction, hit);
        }

        foreach (var candidate in level.GetEntitiesInRange(this.Position, (float)this.Motion.Magnitude + 3))
        {
            if (candidate is not Living { Health: > 0 }
                || ReferenceEquals(candidate, this.Owner) && this.age < 5
                || candidate is IPlayer { GameMode: GameMode.Creative or GameMode.Spectator })
                continue;

            var bounds = candidate.Dimension.CreateBBFromPosition(candidate.Position);
            var grown = new BoundingBox(bounds.Min - new VectorD(0.3f), bounds.Max + new VectorD(0.3f));
            if (MobTerrain.RayIntersection(grown, this.Position, this.Motion) is double hit && hit < fraction)
            {
                fraction = hit;
                target = candidate;
            }
        }

        var next = this.Position + this.Motion * fraction;
        if (!level.TryMoveEntity(this, this.Position, next))
            return;

        this.Position = next;
        this.BoundingBox = this.Dimension.CreateBBFromPosition(this.Position);

        if (fraction < 1 || target != null)
        {
            await this.OnHitAsync(level, target);
            return;
        }

        var water = terrain.GetBlock((Vector)this.Position.Floor())?.RegistryId == BlockIds.Water;
        this.Motion *= water ? 0.8 : 0.99;
        this.Motion -= new VectorD(0, Gravity, 0);

        this.PacketBroadcaster.QueuePacketToLevelInRange(level, this.Position, new TeleportEntityPacket
        {
            EntityId = this.EntityId,
            Position = this.Position,
            Delta = this.Motion,
            Yaw = this.Yaw,
            Pitch = this.Pitch,
            OnGround = false
        });
    }

    // Vanilla's ThrownEgg.onHit: the hit (no damage) and the hatch, then the crack effect, then the egg is gone.
    private async ValueTask OnHitAsync(AbstractLevel level, IEntity? target)
    {
        level.EmitGameEvent(MobGameEvent.ProjectileLand, this.Position, this, this.Owner);

        if (target is Living living)
        {
            await living.DamageCombatAsync(this.Owner ?? this, 0, CombatDamageKind.Projectile, directSource: this);
            if (living is Mob mob && this.Owner != null && mob.IsValidTarget(this.Owner))
                mob.AlertedTarget = this.Owner;
        }

        if (Globals.Random.Next(8) == 0)
        {
            var chicks = Globals.Random.Next(32) == 0 ? 4 : 1;
            for (var i = 0; i < chicks; i++)
            {
                var chick = new Chicken
                {
                    Level = level,
                    EntityId = Server.GetNextEntityId(),
                    Position = this.Position,
                    Yaw = this.Yaw,
                    Age = -24000
                };
                chick.FinalizeHatchling();
                if (!level.TrySpawnEntity(chick, hostOwned: true))
                    break;
            }
        }

        // Entity event 3: the client's egg crack particles, drawn from this egg's item.
        this.PacketBroadcaster.QueuePacketToLevelInRange(level, this.Position, new EntityEventPacket
        {
            EntityId = this.EntityId,
            Event = 3
        });

        await this.RemoveAsync();
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteEntityMetadataType(8, EntityMetadataType.Slot);
        writer.WriteItemStack(this.Item);
    }

    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);

        tag.Set(this.Item.ToNbt("Item"));
        if (this.Owner is { } owner)
            tag.Set(new NbtArray<int>("Owner", EntityNbt.UuidToInts(owner.Uuid)));
    }

    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);

        if (tag.TryGetTag<NbtCompound>("Item", out var item) && item.ItemFromNbt() is ItemStack stack)
            this.Item = new ItemStack(stack, 1);

        this.BoundingBox = this.Dimension.CreateBBFromPosition(this.Position);
    }
}
