using Obsidian.API.Inventory;
using Obsidian.API.Loot;
using Obsidian.Entities.AI;
using Obsidian.Net;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;
using Obsidian.WorldData.Fluids;

namespace Obsidian.Entities;

/// <summary>
/// A fishing bobber, like vanilla's <c>FishingHook</c>: it flies from its owner's rod, lands on water, hooks an
/// entity or item it hits, bobs, and takes bites that its owner reels in for loot.
/// </summary>
/// <remarks>
/// Casting and reeling are vanilla's <c>FishingRodItem.use</c>, reached through <see cref="FishingRodUse"/>.
/// Bobbers are never saved, like vanilla's.
/// </remarks>
[MinecraftEntity("minecraft:fishing_bobber")]
public sealed partial class FishingHook : Entity
{
    // Vanilla's Projectile gravity for the bobber, and its horizontal drag while flying or bobbing.
    private const double Gravity = 0.03;
    private const double Drag = 0.92;

    // The owner must stay within 32 blocks, as vanilla's shouldStopFishing allows.
    private const double MaxOwnerDistanceSquared = 1024;

    // Vanilla discards a bobber that has rested on the ground this long; bobbing in water never times out.
    private const int MaxGroundedLife = 1200;
    private const int MaxOutOfWaterTime = 10;

    private static readonly ParticleDefinition BubbleParticle = new((int)ParticleType.Bubble, "minecraft:bubble");
    private static readonly ParticleDefinition SplashParticle = new((int)ParticleType.Splash, "minecraft:splash");
    private static readonly ParticleDefinition FishingParticle = new((int)ParticleType.Fishing, "minecraft:fishing");

    private enum State { Flying, HookedIn, Bobbing }

    private readonly int luck;
    private readonly int lureTicks;
    private PlayerBase? owner;
    private IEntity? hookedIn;
    private State state = State.Flying;
    private int hookedId;
    private bool biting;
    private int life;
    private int outOfWaterTime;
    private int timeUntilLured;
    private int timeUntilHooked;
    private int nibble;
    private float fishAngle;
    private bool openWater = true;
    private bool leftOwner;

    public FishingHook() => this.Type = EntityType.FishingBobber;

    private FishingHook(PlayerBase owner, int luck, int lureTicks) : this()
    {
        this.owner = owner;
        this.luck = Math.Max(0, luck);
        this.lureTicks = Math.Max(0, lureTicks);
    }

    /// <summary>
    /// Vanilla's <c>FishingHook</c> constructor: the bobber leaves from just ahead of the owner's eyes, along the
    /// look direction at a speed with a little spread. The owner's rod then points at it.
    /// </summary>
    /// <returns>The spawned bobber, or null if the level refused it.</returns>
    internal static FishingHook? Cast(PlayerBase player, AbstractLevel level, int luck, int lureTicks)
    {
        var hook = new FishingHook(player, luck, lureTicks)
        {
            Level = level,
            EntityId = Server.GetNextEntityId(),
        };

        var look = player.ShotDirection(0);
        var horizontal = new VectorD(look.X, 0, look.Z);
        if (horizontal.Magnitude > 0)
            horizontal /= horizontal.Magnitude;

        hook.Position = new VectorD(player.Position.X + horizontal.X * 0.3, player.HeadY, player.Position.Z + horizontal.Z * 0.3);
        var speed = 0.6 / look.Magnitude + 0.5 + (Globals.Random.NextSingle() - Globals.Random.NextSingle()) * 0.0103365;
        hook.Motion = look * speed;
        hook.BoundingBox = hook.Dimension.CreateBBFromPosition(hook.Position);

        if (!level.TrySpawnEntity(hook))
            return null;

        player.Fishing = hook;
        return hook;
    }

    /// <summary>The bobber's owner, if it is a player.</summary>
    internal PlayerBase? Owner => this.owner;

    /// <summary>Whether the bobber is in open water, which vanilla's treasure loot requires.</summary>
    internal bool InOpenWater => this.openWater;

    internal override bool CanUsePortals => false;

    public override void SpawnEntity(Velocity? velocity = null, int additionalData = 0)
    {
        base.SpawnEntity(velocity ?? new Velocity(this.Motion.X, this.Motion.Y, this.Motion.Z), this.owner?.EntityId ?? additionalData);
    }

    public override async ValueTask RemoveAsync()
    {
        if (this.owner is { } player && ReferenceEquals(player.Fishing, this))
            player.Fishing = null;

        await base.RemoveAsync();
    }

    public override async ValueTask TickAsync()
    {
        if (this.Level is not AbstractLevel level || !level.IsMobTicking(this.Position))
            return;

        if (this.owner is not { } player || !OwnerIsFishing(player))
        {
            await this.RemoveAsync();
            return;
        }

        if (!this.leftOwner && !player.BoundingBox.Intersects(this.BoundingBox))
            this.leftOwner = true;

        var terrain = new MobTerrain(level);
        var inWater = terrain.IsInFluid(this.Position, water: true);

        // Vanilla: the lifetime counts only while the bobber is on the ground (from the previous tick's movement).
        var grounded = !inWater && this.MovementFlags.HasFlag(MovementFlags.OnGround);
        if (grounded && ++this.life >= MaxGroundedLife)
        {
            await this.RemoveAsync();
            return;
        }

        if (!grounded)
            this.life = 0;

        if (this.state == State.Flying && this.hookedIn is not null)
            this.state = State.HookedIn;

        if (this.state == State.HookedIn)
        {
            this.MovementFlags = MovementFlags.None;
            this.FollowHookedEntity();
            this.Broadcast(level);
            return;
        }

        if (this.state == State.Flying)
        {
            if (inWater)
            {
                // Landing on water damps the flight and starts the bobbing.
                this.Motion = new VectorD(this.Motion.X * 0.3, this.Motion.Y * 0.2, this.Motion.Z * 0.3);
                this.state = State.Bobbing;
                this.MovementFlags = MovementFlags.None;
                this.Broadcast(level);
                return;
            }

            this.CheckCollision(level, terrain);
        }
        else
        {
            if (!inWater)
            {
                this.state = State.Flying;
            }
            else
            {
                this.Bob(level, terrain);
            }
        }

        if (this.state == State.Flying && !inWater)
            this.Motion -= new VectorD(0, Gravity, 0);

        // A downward move blocked by terrain leaves the bobber on the ground, as vanilla's move() does.
        var landed = !inWater && this.Motion.Y < 0;
        var next = this.Position + this.Motion;
        if (level.TryMoveEntity(this, this.Position, next))
        {
            this.Position = next;
            this.BoundingBox = this.Dimension.CreateBBFromPosition(this.Position);
            this.MovementFlags = MovementFlags.None;
        }
        else
        {
            this.Motion = VectorD.Zero;
            this.MovementFlags = landed ? MovementFlags.OnGround : MovementFlags.None;
        }

        this.Motion *= Drag;
        this.Broadcast(level);
    }

    /// <summary>
    /// Vanilla's <c>shouldStopFishing</c>: the owner is still alive, holds a rod in either hand, and is near enough.
    /// </summary>
    private bool OwnerIsFishing(PlayerBase player)
    {
        if (!player.Alive)
            return false;

        var rodHeld = IsRod(player.GetHeldItem()) || IsRod(player.GetOffHandItem());
        var offset = player.Position - this.Position;
        return rodHeld && offset.Magnitude * offset.Magnitude <= MaxOwnerDistanceSquared;
    }

    private static bool IsRod(ItemStack? stack) => stack is { Count: > 0 } && ItemUseBehaviors.For(stack) is FishingRodUse;

    /// <summary>
    /// Vanilla's flight collision: the nearest block or hookable entity along the motion. A block shortens the motion
    /// to the hit point, and an entity hooks the bobber, which then follows it.
    /// </summary>
    private void CheckCollision(AbstractLevel level, MobTerrain terrain)
    {
        if (this.Motion.Magnitude == 0)
            return;

        var end = this.Position + this.Motion;
        var swept = new BoundingBox(
            VectorD.Min(this.Position, end) - new VectorD(0.3f),
            VectorD.Max(this.Position, end) + new VectorD(0.3f));

        var fraction = 1d;
        foreach (var shape in terrain.GetCollisions(swept))
        {
            if (MobTerrain.RayIntersection(shape, this.Position, this.Motion) is double hit)
                fraction = Math.Min(fraction, hit);
        }

        IEntity? target = null;
        foreach (var candidate in level.GetEntitiesInRange(this.Position, (float)this.Motion.Magnitude + 3))
        {
            if (!this.CanHit(candidate))
                continue;

            var bounds = candidate.Dimension.CreateBBFromPosition(candidate.Position);
            var grown = new BoundingBox(bounds.Min - new VectorD(0.3f), bounds.Max + new VectorD(0.3f));
            if (MobTerrain.RayIntersection(grown, this.Position, this.Motion) is double hit && hit < fraction)
            {
                fraction = hit;
                target = candidate;
            }
        }

        if (target is not null)
        {
            // Vanilla's onHitEntity: the entity is hooked, and the bobber stops where it reached it.
            this.Motion = VectorD.Zero;
            this.SetHookedIn(target);
            return;
        }

        // Vanilla's onHitBlock: the motion keeps its direction and shrinks to the distance to the hit.
        if (fraction < 1)
            this.Motion *= fraction;
    }

    // Vanilla's canHitEntity: anything alive that can be picked, plus items, but never the owner until the bobber has left it.
    private bool CanHit(IEntity candidate)
    {
        if (ReferenceEquals(candidate, this.owner) && !this.leftOwner)
            return false;

        return candidate switch
        {
            Living living => living.Health > 0 && candidate is not IPlayer { GameMode: GameMode.Spectator },
            ItemEntity item => !item.Removed,
            _ => false
        };
    }

    private void FollowHookedEntity()
    {
        if (this.hookedIn is { } target && (target is Living { Health: > 0 } || target is ItemEntity { Removed: false })
            && ReferenceEquals(target.Level, this.Level))
        {
            this.Position = new VectorD(target.Position.X, target.Position.Y + 0.8 * target.Dimension.Height, target.Position.Z);
            this.BoundingBox = this.Dimension.CreateBBFromPosition(this.Position);
            this.Motion = VectorD.Zero;
            return;
        }

        this.SetHookedIn(null);
        this.state = State.Flying;
    }

    /// <summary>
    /// Vanilla's bobbing: the bobber rides the water surface, sinks while a fish bites, and runs the bite timers
    /// through <see cref="CatchFish"/> whenever it is in water.
    /// </summary>
    private void Bob(AbstractLevel level, MobTerrain terrain)
    {
        var block = (Vector)this.Position.Floor();
        var surface = WaterSurfaceHeight(terrain, block);
        var delta = this.Position.Y + this.Motion.Y - block.Y - surface;
        if (Math.Abs(delta) < 0.01)
            delta += delta < 0 ? -0.1 : 0.1;

        this.Motion = new VectorD(
            this.Motion.X * 0.9,
            this.Motion.Y - delta * Globals.Random.NextSingle() * 0.2,
            this.Motion.Z * 0.9);

        if (this.nibble <= 0 && this.timeUntilHooked <= 0)
            this.openWater = true;
        else
            this.openWater = this.openWater && this.outOfWaterTime < MaxOutOfWaterTime && IsOpenWater(terrain, block);

        if (terrain.IsInFluid(this.Position, water: true))
        {
            this.outOfWaterTime = Math.Max(0, this.outOfWaterTime - 1);
            if (this.biting)
            {
                var sink = Globals.Random.NextSingle();
                this.Motion -= new VectorD(0, 0.1 * sink * sink, 0);
            }

            this.CatchFish(level, terrain, block);
        }
        else
        {
            this.outOfWaterTime = Math.Min(MaxOutOfWaterTime, this.outOfWaterTime + 1);
        }
    }

    /// <summary>
    /// Vanilla's <c>catchingFish</c>: the lure countdown, then the hook countdown, then a bite the owner must reel in.
    /// Rain speeds the countdowns and an unexposed spot slows them.
    /// </summary>
    private void CatchFish(AbstractLevel level, MobTerrain terrain, Vector block)
    {
        var change = 1;
        if (Globals.Random.NextSingle() < 0.25f && terrain.IsRainingAt(block))
            change++;
        // Water dims sky light, so the bobber's own block is never 15; the block above is open to the sky when it is.
        if (Globals.Random.NextSingle() < 0.5f && terrain.GetSkyLight(new Vector(block.X, block.Y + 1, block.Z)) < 15)
            change--;

        if (this.nibble > 0)
        {
            this.nibble--;
            if (this.nibble <= 0)
            {
                this.timeUntilLured = 0;
                this.timeUntilHooked = 0;
                this.SetBiting(false);
            }
        }
        else if (this.timeUntilHooked > 0)
        {
            this.timeUntilHooked -= change;
            if (this.timeUntilHooked > 0)
            {
                // The fish is circling: the bobber's ripples follow the angle the fish is at.
                this.fishAngle += (Globals.Random.NextSingle() - Globals.Random.NextSingle()) * 9.188f;
                var angle = this.fishAngle * MathF.PI / 180f;
                var ripple = new VectorD(
                    this.Position.X + MathF.Sin(angle) * this.timeUntilHooked * 0.1f,
                    Math.Floor(this.Position.Y) + 1,
                    this.Position.Z + MathF.Cos(angle) * this.timeUntilHooked * 0.1f);
                if (Globals.Random.NextSingle() < 0.15f)
                    level.SpawnParticles(BubbleParticle, new VectorD(ripple.X, ripple.Y - 0.1, ripple.Z), 1, default, 0);
                level.SpawnParticles(FishingParticle, ripple, 1, default, 0);
            }
            else
            {
                // The bite: a splash, then the owner has a window to reel in.
                PlaySound(level, this.Position, "minecraft:entity.fishing_bobber.splash", 0.25f, 1f + (Globals.Random.NextSingle() - Globals.Random.NextSingle()) * 0.4f);
                var biteCount = 1 + (int)(Globals.Random.NextSingle() * 20f);
                var biteSpread = Globals.Random.NextSingle();
                var biteAt = new VectorD(this.Position.X, Math.Floor(this.Position.Y) + 1, this.Position.Z);
                level.SpawnParticles(BubbleParticle, biteAt, biteCount, new VectorF(biteSpread, 0, biteSpread), 0);
                level.SpawnParticles(FishingParticle, biteAt, biteCount, new VectorF(biteSpread, 0, biteSpread), 0.2f);
                this.nibble = Globals.Random.Next(20, 40);
                this.SetBiting(true);
            }
        }
        else if (this.timeUntilLured > 0)
        {
            this.timeUntilLured -= change;
            var chance = 0.15f;
            if (this.timeUntilLured < 20)
                chance += (20 - this.timeUntilLured) * 0.05f;
            else if (this.timeUntilLured < 40)
                chance += (40 - this.timeUntilLured) * 0.02f;
            else if (this.timeUntilLured < 60)
                chance += (60 - this.timeUntilLured) * 0.01f;

            if (Globals.Random.NextSingle() < chance)
                level.SpawnParticles(SplashParticle, new VectorD(this.Position.X, Math.Floor(this.Position.Y) + 1, this.Position.Z), 2 + Globals.Random.Next(2), new VectorF(0.1f, 0, 0.1f), 0);

            if (this.timeUntilLured <= 0)
            {
                this.fishAngle = Globals.Random.NextSingle() * 360f;
                this.timeUntilHooked = Globals.Random.Next(20, 80);
            }
        }
        else
        {
            // A fish is on its way: a random wait, shortened by the rod's Lure.
            this.timeUntilLured = Globals.Random.Next(100, 600) - this.lureTicks;
        }
    }

    /// <summary>
    /// Vanilla's <c>retrieve</c>: reels the bobber in. Returns the rod's damage: hooked entities and items cost more,
    /// a caught fish costs one, and a grounded bobber costs two.
    /// </summary>
    internal async ValueTask<int> RetrieveAsync(ItemStack rod)
    {
        var damage = 0;
        if (this.owner is { } player && this.Level is AbstractLevel level && OwnerIsFishing(player))
        {
            if (this.hookedIn is { } target)
            {
                this.PullEntity(target, player);
                damage = target is ItemEntity ? 3 : 5;
            }
            else if (this.nibble > 0)
            {
                this.DropCatch(level, player, rod);
                damage = 1;
            }
        }

        // Vanilla's retrieve: a bobber reeled in while on the ground costs two damage. Water never sets OnGround.
        if (this.MovementFlags.HasFlag(MovementFlags.OnGround))
            damage = 2;

        await this.RemoveAsync();
        return damage;
    }

    // Vanilla's pull: the hooked entity moves a tenth of the way towards the owner each reel.
    private void PullEntity(IEntity hooked, PlayerBase player)
    {
        if (this.Level is not AbstractLevel level || hooked is not Entity target)
            return;

        target.Motion += (player.Position - this.Position) * 0.1;
        level.PacketBroadcaster.QueuePacketToLevel(level, new SetEntityMotionPacket
        {
            EntityId = target.EntityId,
            Velocity = new(target.Motion.X, target.Motion.Y, target.Motion.Z)
        });
    }

    // Vanilla's loot for a bite: the fishing loot table, the items flying to the owner, and the experience orb.
    private void DropCatch(AbstractLevel level, PlayerBase player, ItemStack rod)
    {
        var resolve = PluginLootTables.CaptureResolver();
        if (resolve("minecraft:gameplay/fishing") is not LootTable table)
            return;

        var context = new LootContext
        {
            EntityVillagerVariant = LootEntityComponents.VillagerVariant,
            Random = table.CreateRandom(0, BlockLoot.SequencesFor(level)),
            Luck = this.luck + (float)player.GetAttributeValue("minecraft:luck"),
            Origin = this.Position,
            ThisEntity = this,
            Tool = rod,
            EntityFishingOpenWater = entity => entity is FishingHook hook ? hook.InOpenWater : null,
            OriginBiome = position => level.GetLoadedChunk(((int)Math.Floor(position.X)) >> 4, ((int)Math.Floor(position.Z)) >> 4) is { } chunk
                ? chunk.GetBiome((int)Math.Floor(position.X), (int)Math.Floor(position.Y), (int)Math.Floor(position.Z)).Name
                : null,
            ResolveTable = resolve
        };

        foreach (var stack in table.GetRandomItems(context))
        {
            var toOwner = player.Position - this.Position;
            var item = new ItemEntity
            {
                Level = level,
                EntityId = Server.GetNextEntityId(),
                Position = this.Position,
                Item = stack,
                Motion = new VectorD(
                    toOwner.X * 0.1,
                    toOwner.Y * 0.1 + Math.Sqrt(toOwner.Magnitude) * 0.08,
                    toOwner.Z * 0.1)
            };
            level.TrySpawnEntity(item, hostOwned: true);

            if (FishingData.IsFish(stack))
                player.Statistics.Increment("minecraft:fish_caught");
        }

        level.TrySpawnEntity(new ExperienceOrb
        {
            Level = level,
            EntityId = Server.GetNextEntityId(),
            Position = new VectorD(player.Position.X, player.Position.Y + 0.5, player.Position.Z),
            Value = Globals.Random.Next(6) + 1
        }, hostOwned: true);
    }

    // The bobber's synced data (entities.json): the hooked entity's id plus one, and whether a fish is biting.
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(8, EntityMetadataType.VarInt);
        writer.WriteVarInt(this.hookedId);
        writer.WriteEntityMetadataType(9, EntityMetadataType.Boolean);
        writer.WriteBoolean(this.biting);
    }

    private void SetHookedIn(IEntity? target)
    {
        this.hookedIn = target;
        this.hookedId = target is null ? 0 : target.EntityId + 1;
        if (this.Level is AbstractLevel level)
            level.PacketBroadcaster.QueuePacketToLevelInRange(level, this.Position, new SetEntityDataPacket { EntityId = EntityId, Entity = this });
    }

    private void SetBiting(bool value)
    {
        if (this.biting == value)
            return;

        this.biting = value;
        if (this.Level is AbstractLevel level)
            level.PacketBroadcaster.QueuePacketToLevelInRange(level, this.Position, new SetEntityDataPacket { EntityId = EntityId, Entity = this });
    }

    private void Broadcast(AbstractLevel level) =>
        level.PacketBroadcaster.QueuePacketToLevelInRange(level, this.Position, new TeleportEntityPacket
        {
            EntityId = this.EntityId,
            Position = this.Position,
            Delta = this.Motion,
            Yaw = this.Yaw,
            Pitch = this.Pitch,
            OnGround = false
        });

    // Vanilla's FishingHook.isOpenWater: every layer of water from the hook's column out to two blocks must be
    // unbroken, so the bobber sits in a clear pool rather than a shallow or covered one.
    private static bool IsOpenWater(MobTerrain terrain, Vector block)
    {
        var previous = OpenWaterType.Invalid;
        for (var dy = -1; dy <= 2; dy++)
        {
            var type = OpenWaterTypeOfArea(terrain, block + new Vector(-2, dy, -2), block + new Vector(2, dy, 2));
            switch (type)
            {
                case OpenWaterType.AboveWater when previous == OpenWaterType.Invalid:
                    return false;
                case OpenWaterType.InsideWater when previous == OpenWaterType.AboveWater:
                    return false;
                case OpenWaterType.Invalid:
                    return false;
            }

            previous = type;
        }

        return true;
    }

    private static OpenWaterType OpenWaterTypeOfArea(MobTerrain terrain, Vector min, Vector max)
    {
        var result = (OpenWaterType?)null;
        for (var x = min.X; x <= max.X; x++)
        {
            for (var y = min.Y; y <= max.Y; y++)
            {
                for (var z = min.Z; z <= max.Z; z++)
                {
                    var type = OpenWaterTypeOfBlock(terrain, new Vector(x, y, z));
                    result = result is null || result == type ? type : OpenWaterType.Invalid;
                }
            }
        }

        return result ?? OpenWaterType.Invalid;
    }

    private static OpenWaterType OpenWaterTypeOfBlock(MobTerrain terrain, Vector position)
    {
        if (terrain.GetBlock(position) is not { } block)
            return OpenWaterType.Invalid;
        if (block.IsAir || block.RegistryId == BlockIds.LilyPad)
            return OpenWaterType.AboveWater;

        // Only source water with an empty collision shape counts as open; a waterlogged obstruction does not.
        var fluid = FluidState.Of(block);
        return fluid.IsWater && fluid.IsSource && BlockCollisionShapes.Get(block).Count == 0
            ? OpenWaterType.InsideWater
            : OpenWaterType.Invalid;
    }

    // The height of the water surface in a block, or 0 when it has no water.
    private static double WaterSurfaceHeight(MobTerrain terrain, Vector block)
    {
        if (terrain.GetBlock(block) is not { } current || !FluidState.Of(current).IsWater)
            return 0;

        var above = terrain.GetBlock(block + Vector.Up);
        return above is not null && FluidState.Of(above).IsWater ? 1 : FluidState.Of(current).OwnHeight;
    }

    private static void PlaySound(AbstractLevel level, VectorD position, string sound, float volume, float pitch) =>
        level.PacketBroadcaster.BroadcastToLevelInRange(level, (Vector)position.Floor(), new SoundPacket
        {
            SoundLocation = sound,
            SoundPosition = new SoundPosition(position.X, position.Y, position.Z),
            Category = SoundCategory.Neutral,
            Volume = volume,
            Pitch = pitch,
            Seed = Globals.Random.NextInt64()
        });

    private enum OpenWaterType { AboveWater, InsideWater, Invalid }
}
