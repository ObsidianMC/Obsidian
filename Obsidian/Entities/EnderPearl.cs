using Obsidian.API.Inventory;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Entities;

/// <summary>
/// A thrown ender pearl, like vanilla's <c>ThrownEnderpearl</c>: it flies along its trajectory, travels through
/// portals and End gateways, and teleports its owner to where it lands (even across dimensions), dealing 5 fall
/// damage and occasionally spawning an endermite.
/// </summary>
[MinecraftEntity("minecraft:ender_pearl")]
public sealed partial class EnderPearl : Throwable
{
    private static readonly ParticleDefinition PortalParticle = new((int)ParticleType.Portal, "minecraft:portal");

    internal IEntity? Owner { get; set; }

    private Guid ownerUuid;

    private int age;

    private bool resolveOwner = true;

    public EnderPearl() => this.Type = EntityType.EnderPearl;

    /// <summary>
    /// The pearl item clients show; an ender pearl unless thrown with another, like vanilla's.
    /// </summary>
    public ItemStack? Item { get; internal set; } = new(ItemsRegistry.EnderPearl, 1);

    /// <summary>
    /// Vanilla <c>EnderpearlItem.use</c>: throws an ender pearl from <paramref name="player"/> when not on cooldown,
    /// starting the item's cooldown and using up one pearl unless the player is in creative.
    /// </summary>
    /// <returns>Whether an ender pearl was thrown.</returns>
    internal static async ValueTask<bool> ThrowAsync(PlayerBase player, InteractionHand hand, ItemStack stack)
    {
        if (!player.Alive
            || player.Respawning
            || player.GameMode == GameMode.Spectator
            || player.IsOnCooldown(stack)
            || player.Level is not AbstractLevel level)
        {
            return false;
        }

        var pearl = new EnderPearl
        {
            Level = level,
            EntityId = Server.GetNextEntityId(),
            Owner = player,
            ownerUuid = player.Uuid,
            Position = new VectorD(player.Position.X, player.HeadY - 0.1, player.Position.Z),
            Motion = player.ShotDirection(0) * 1.5 + player.CombatMotion,
            Item = new ItemStack(stack, 1)
        };
        pearl.BoundingBox = pearl.Dimension.CreateBBFromPosition(pearl.Position);

        if (!level.TryAddEntity(pearl))
            return false;

        pearl.SpawnEntity();

        level.PacketBroadcaster.BroadcastToLevelInRange(level, (Vector)player.Position.Floor(), new SoundPacket
        {
            SoundLocation = "minecraft:entity.ender_pearl.throw",
            SoundPosition = new SoundPosition(player.Position.X, player.Position.Y, player.Position.Z),
            Category = SoundCategory.Neutral,
            Volume = 0.5f,
            Pitch = 0.4f / (Globals.Random.NextSingle() * 0.4f + 0.8f),
            Seed = Globals.Random.NextInt64()
        });

        await player.StartItemCooldownAsync(stack);

        if (player.GameMode != GameMode.Creative)
        {
            var slot = hand == InteractionHand.OffHand ? 45 : player.CurrentHeldItemSlot;
            player.Inventory.RemoveItem(slot, 1);
            await player.SyncInventorySlotAsync(slot);
        }

        player.Statistics.Increment(stack.Holder.UnlocalizedName, type: "minecraft:used");
        return true;
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

        if (this.Owner == null && this.ownerUuid != Guid.Empty && (this.resolveOwner || this.age % 20 == 0))
            this.Owner = FindOwner(level, this.ownerUuid);

        this.resolveOwner = false;

        if (this.Owner is PlayerBase { Connected: false } or PlayerBase { Alive: false }
            || this.Owner is IPlayer { Alive: false })
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

        foreach (var candidate in level.GetEntitiesInRange(this.Position, (float)this.Motion.Magnitude + 20))
        {
            if (candidate is not Living and not EndCrystal
                || candidate.Health <= 0
                || ReferenceEquals(candidate, this.Owner) && this.age < 5
                || candidate is IPlayer { GameMode: GameMode.Creative or GameMode.Spectator })
            {
                continue;
            }

            if (candidate is EnderDragon dragon)
            {
                foreach (var part in dragon.Parts)
                {
                    if (MobTerrain.RayIntersection(part.BoundingBox, this.Position, this.Motion) is double partHit && partHit < fraction)
                    {
                        fraction = partHit;
                        target = part;
                    }
                }

                continue;
            }

            var bounds = candidate.Dimension.CreateBBFromPosition(candidate.Position);
            if (MobTerrain.RayIntersection(new BoundingBox(bounds.Min - new VectorD(0.3f), bounds.Max + new VectorD(0.3f)), this.Position, this.Motion) is double hit
                && hit < fraction)
            {
                if (candidate is Enderman enderman)
                {
                    enderman.TryAvoidProjectile();
                    continue;
                }

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
        if (!this.NoGravity)
            this.Motion -= new VectorD(0, 0.03, 0);

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

    private async ValueTask OnHitAsync(AbstractLevel level, IEntity? target)
    {
        level.EmitGameEvent(MobGameEvent.ProjectileLand, this.Position, this, this.Owner);

        if (target != null)
        {
            if (target is EnderDragonPart part)
                await part.Parent.DamagePartAsync(this.Owner ?? this, 0, part.Index, projectile: true);
            else if (target is Living living)
                await living.DamageCombatAsync(this.Owner ?? this, 0, CombatDamageKind.Projectile, directSource: this);
            else
                await target.DamageAsync(this.Owner ?? this, 0);

            if (target is Mob mob && this.Owner != null && mob.IsValidTarget(this.Owner))
                mob.AlertedTarget = this.Owner;
        }

        level.SpawnParticles(PortalParticle, this.Position, 32, new VectorF(0.5f, 1.0f, 0.5f), 1.0f);

        var owner = this.Owner ?? (this.ownerUuid != Guid.Empty ? FindOwner(level, this.ownerUuid) : null);
        if (owner is PlayerBase player && player.Connected && player.Alive && !player.Respawning)
        {
            if (Globals.Random.NextSingle() < 0.05f
                && level.LevelData.Difficulty != Difficulty.Peaceful
                && level.LevelData.GetBooleanRule("spawn_mobs"))
            {
                var endermite = new Endermite
                {
                    Level = level,
                    EntityId = Server.GetNextEntityId(),
                    Position = this.Position,
                    Yaw = player.Yaw,
                    Pitch = player.Pitch
                };
                endermite.InitializeAi();
                if (level.TryAddEntity(endermite, hostOwned: true))
                    endermite.SpawnEntity();
            }

            player.LeaveVehicle();
            await player.TeleportAsync(level, this.Position, player.Yaw, player.Pitch);
            player.FallDistance = 0;
            await player.DamageCombatAsync(this, 5, CombatDamageKind.Fall);

            level.PacketBroadcaster.BroadcastToLevelInRange(level, (Vector)this.Position.Floor(), new SoundPacket
            {
                SoundLocation = "minecraft:entity.player.teleport",
                SoundPosition = new SoundPosition(this.Position.X, this.Position.Y, this.Position.Z),
                Category = SoundCategory.Players,
                Volume = 1,
                Pitch = 1,
                Seed = Globals.Random.NextInt64()
            });
        }
        else if (owner is Entity entity && entity.Health > 0)
        {
            entity.LeaveVehicle();
            await entity.TeleportAsync(level, this.Position, entity.Yaw, entity.Pitch);
        }

        await this.RemoveAsync();
    }

    private IEntity? FindOwner(AbstractLevel level, Guid uuid)
    {
        if (level.Players.TryGetValue(uuid, out var localPlayer))
            return localPlayer;

        var world = level is IDimension dimension ? dimension.ParentWorld as World : level as World;
        if (world != null)
        {
            if (world.Players.TryGetValue(uuid, out var worldPlayer))
                return worldPlayer;

            foreach (var dim in world.dimensions.Values)
            {
                if (dim.Players.TryGetValue(uuid, out var dimPlayer))
                    return dimPlayer;
            }
        }

        return level.FindEntity(uuid);
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

        tag.SetOrRemove("Item", this.Item?.ToNbt("Item"));
        var uuid = this.Owner?.Uuid ?? this.ownerUuid;
        if (uuid != Guid.Empty)
            tag.Set(new NbtArray<int>("Owner", EntityNbt.UuidToInts(uuid)));
        else
            tag.Remove("Owner");
    }

    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);

        this.Item = tag.TryGetTag<NbtCompound>("Item", out var item) && item.ItemFromNbt() is ItemStack stack
            ? new ItemStack(stack, 1)
            : new ItemStack(ItemsRegistry.EnderPearl, 1);

        if (tag.TryGetTag<NbtArray<int>>("Owner", out var owner) && owner.Count == 4)
            this.ownerUuid = EntityNbt.UuidFromInts(owner.GetArray());
        else
            this.ownerUuid = Guid.Empty;

        this.BoundingBox = this.Dimension.CreateBBFromPosition(this.Position);
    }
}
