using Obsidian.API.Entities;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Plugins;
using Obsidian.WorldData;

namespace Obsidian.Entities;

/// <summary>
/// A projectile or object of a plugin's entity type (see <see cref="EntityKind"/>): it moves along its motion, slowed
/// by drag and pulled down by its type's gravity; a projectile hits the first block or living entity in its way, and an
/// object comes to rest where it lands.
/// </summary>
/// <param name="definition">The entity's type.</param>
internal sealed class PluginEntity(EntityDefinition definition) : Entity
{
    // Like vanilla's thrown projectiles: they slow by 1% a tick, and don't hit their owner at first.
    private const float Drag = 0.99f;
    private const int OwnerImmunityTicks = 5;

    private Guid ownerUuid;

    // The owner found by its UUID, held weakly: a projectile mustn't keep a removed owner, or what it holds, alive.
    private WeakReference<IEntity>? owner;

    private int age;
    private int nextOwnerSearch;
    private bool removed;
    private bool resting;

    public override EntityDefinition Definition => definition;

    // Read from the type, whose size a reload may change.
    public override EntityDimension Dimension
    {
        get => new() { Width = definition.Settings.Width, Height = definition.Settings.Height };
        protected set { }
    }

    /// <summary>
    /// The entity that shot the projectile, if it's in the level.
    /// </summary>
    internal IEntity? Owner
    {
        get
        {
            if (this.owner?.TryGetTarget(out var known) == true && known.Level == this.Level)
                return known;

            // An owner that isn't around, like a player who left, is looked for again once a second.
            if (this.ownerUuid == Guid.Empty
                || this.age < this.nextOwnerSearch
                || this.Level is not AbstractLevel level)
            {
                return null;
            }

            var found = FindEntity(level, this.ownerUuid);
            this.owner = found is null ? null : new WeakReference<IEntity>(found);

            if (found is null)
                this.nextOwnerSearch = this.age + 20;

            return found;
        }
    }

    internal void SetOwner(IEntity? shooter)
    {
        this.ownerUuid = shooter?.Uuid ?? Guid.Empty;
        this.owner = shooter is null ? null : new WeakReference<IEntity>(shooter);
    }

    // Like vanilla's projectiles, a projectile's spawn data is its owner's entity id, which clients aim it with; sent
    // to every client that starts tracking it, not only those there when it spawned.
    internal override IClientboundPacket CreateSpawnPacket(Velocity? velocity = null, int additionalData = 0) =>
        base.CreateSpawnPacket(
            velocity ?? new Velocity(this.Motion.X, this.Motion.Y, this.Motion.Z),
            this.Owner?.EntityId ?? 0);

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        this.WritePluginData(writer);
    }

    public override async ValueTask RemoveAsync()
    {
        this.removed = true;
        await base.RemoveAsync();
    }

    public override async ValueTask TickAsync()
    {
        if (this.Level is not AbstractLevel level || !level.IsMobTicking(this.Position))
            return;

        this.age++;

        // Like vanilla, entities that fall out of the world are gone.
        if (this.Position.Y < -128)
        {
            await this.RemoveAsync();
            return;
        }

        await this.RunBehaviorAsync(behavior => behavior.TickAsync(this));

        if (this.removed)
            return;

        if (this.DataChanged)
        {
            this.DataChanged = false;
            this.PacketBroadcaster.QueuePacketToLevelInRange(this.Level, this.Position, new SetEntityDataPacket
            {
                EntityId = this.EntityId,
                Entity = this
            });
        }

        if (definition.Kind == EntityKind.Projectile)
            await this.FlyAsync(level);
        else
            this.Fall(level);
    }

    // Like vanilla's thrown projectiles: move along the motion until a block or living entity is in the way, which the
    // projectile hits and is gone; then slow by drag and fall by gravity.
    private async ValueTask FlyAsync(AbstractLevel level)
    {
        var motion = this.Motion;

        if (motion.Magnitude >= 1e-4)
        {
            var (fraction, target, block) = this.Sweep(motion);
            var from = this.Position;
            var to = from + motion * fraction;

            if (!level.TryMoveEntity(this, from, to))
                return;

            this.Position = to;
            this.BoundingBox = this.Dimension.CreateBBFromPosition(to);
            this.Broadcast(motion, onGround: false);

            if (fraction < 1)
            {
                var hit = new EntityHit
                {
                    Entity = target,
                    Block = target is null ? block : null,
                    Position = to,
                    Owner = this.Owner
                };

                await this.RunBehaviorAsync(behavior => behavior.HitAsync(this, hit));
                await this.RemoveAsync();
                return;
            }
        }

        this.Motion = motion * Drag - new VectorD(0, this.Gravity, 0);
    }

    // Like vanilla moves entities: as a box, clipped against the blocks' shapes one axis at a time, so that it slides
    // along and rests on surfaces; it comes to rest where it lands, and stays while something holds it up.
    private void Fall(AbstractLevel level)
    {
        var terrain = new MobTerrain(this.Level);
        var bounds = this.Dimension.CreateBBFromPosition(this.Position);
        var motion = this.Motion;

        // Held up when even a tiny fall is clipped; blocks it's already inside don't hold it.
        var nudge = new VectorD(0, -1e-3, 0);
        var below = terrain.GetCollisions(bounds.OffsetBy(nudge)).ToList();
        var supported = EntityMovement.Clip(bounds, nudge, below).Y > nudge.Y;

        if (supported && motion.Y <= 0 && Math.Abs(motion.X) < 1e-4 && Math.Abs(motion.Z) < 1e-4)
        {
            this.Motion = VectorD.Zero;

            if (!this.resting)
                this.Broadcast(VectorD.Zero, onGround: true);

            this.resting = true;
            return;
        }

        var reach = bounds.OffsetBy(motion);
        var swept = new BoundingBox(VectorD.Min(bounds.Min, reach.Min), VectorD.Max(bounds.Max, reach.Max));
        var moved = EntityMovement.Clip(bounds, motion, terrain.GetCollisions(swept).ToList());

        var from = this.Position;
        var to = from + moved;
        if (!level.TryMoveEntity(this, from, to))
            return;

        this.Position = to;
        this.BoundingBox = this.Dimension.CreateBBFromPosition(to);

        // A blocked axis stops, against a ceiling too; landing stops it all.
        var landed = motion.Y < 0 && moved.Y > motion.Y + 1e-7;
        this.Motion = landed
            ? VectorD.Zero
            : new VectorD(
                moved.X == motion.X ? motion.X * Drag : 0,
                (moved.Y == motion.Y ? motion.Y * Drag : 0) - this.Gravity,
                moved.Z == motion.Z ? motion.Z * Drag : 0);

        if (to != from || landed != this.resting)
            this.Broadcast(this.Motion, onGround: landed);

        this.resting = landed;
    }

    private void Broadcast(VectorD motion, bool onGround) =>
        this.PacketBroadcaster.QueuePacketToLevelInRange(this.Level, this.Position, new TeleportEntityPacket
        {
            EntityId = this.EntityId,
            Position = this.Position,
            Delta = motion,
            Yaw = this.Yaw,
            Pitch = this.Pitch,
            OnGround = onGround
        });

    // How far along its motion the projectile gets before a block or living entity stops it, the entity it hits and
    // the block it hits.
    private (double Fraction, IEntity? Target, Vector? Block) Sweep(VectorD motion)
    {
        var terrain = new MobTerrain(this.Level);
        var end = this.Position + motion;
        var swept = new BoundingBox(
            VectorD.Min(this.Position, end) - new VectorD(0.3f),
            VectorD.Max(this.Position, end) + new VectorD(0.3f));

        var fraction = 1d;
        Vector? block = null;

        foreach (var (shape, position) in terrain.GetBlockCollisions(swept))
        {
            if (MobTerrain.RayIntersection(shape, this.Position, motion) is double hit && hit < fraction)
            {
                fraction = hit;
                block = position;
            }
        }

        IEntity? target = null;
        var shooter = this.Owner;

        foreach (var candidate in this.Level.GetEntitiesInRange(this.Position, (float)motion.Magnitude + 3))
        {
            if (candidate is not Living
                || candidate.Health <= 0
                || ReferenceEquals(candidate, shooter) && this.age <= OwnerImmunityTicks
                || candidate is IPlayer { GameMode: GameMode.Creative or GameMode.Spectator })
            {
                continue;
            }

            var bounds = candidate.Dimension.CreateBBFromPosition(candidate.Position);
            var inflated = new BoundingBox(bounds.Min - new VectorD(0.3f), bounds.Max + new VectorD(0.3f));

            if (MobTerrain.RayIntersection(inflated, this.Position, motion) is double hit && hit < fraction)
            {
                fraction = hit;
                target = candidate;
            }
        }

        return (fraction, target, block);
    }

    // An entity of the level by its UUID: a player, or an entity of a loaded region.
    private static IEntity? FindEntity(AbstractLevel level, Guid uuid)
    {
        if (level.Players.TryGetValue(uuid, out var player))
            return player;

        return level.Regions.Values
            .OfType<Region>()
            .SelectMany(region => region.Entities.Values)
            .FirstOrDefault(entity => entity.Uuid == uuid);
    }

    // The type's gravity, unless the entity was saved without gravity.
    private float Gravity => this.NoGravity ? 0 : definition.Settings.Gravity;

    /// <returns>Whether the type's behaviour used the interaction.</returns>
    internal async ValueTask<bool> InteractAsync(IPlayer player, InteractionHand hand)
    {
        var used = false;
        await this.RunBehaviorAsync(async behavior => used = await behavior.InteractAsync(this, player, hand));
        return used;
    }

    // Runs the type's behaviour as a call into its plugin, so that the plugin can't be released meanwhile. The
    // behaviour may move or turn the entity: the level learns of a move first (one it can't make, into an unloaded
    // region say, is undone), and clients of both.
    private async ValueTask RunBehaviorAsync(Func<IEntityBehavior, ValueTask> run)
    {
        if (definition.Binding is not { Behavior: IEntityBehavior behavior, Owner: PluginContainer plugin })
            return;

        var (position, yaw, pitch) = (this.Position, this.Yaw, this.Pitch);

        using (var call = plugin.TryEnter())
        {
            if (call is null)
                return;

            await run(behavior);
        }

        if (this.removed || this.Level is not AbstractLevel level)
            return;

        if (this.Position != position && !level.TryMoveEntity(this, position, this.Position))
            this.Position = position;

        if (this.Position != position || this.Yaw != yaw || this.Pitch != pitch)
        {
            this.BoundingBox = this.Dimension.CreateBBFromPosition(this.Position);
            this.Broadcast(this.Motion, onGround: this.resting);
        }
    }

    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);

        if (this.ownerUuid != Guid.Empty)
            tag.Set(new NbtArray<int>("Owner", EntityNbt.UuidToInts(this.ownerUuid)));
    }

    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);

        if (tag.TryGetTag<NbtArray<int>>("Owner", out var savedOwner) && savedOwner.Count == 4)
            this.ownerUuid = EntityNbt.UuidFromInts(savedOwner.GetArray());

        this.BoundingBox = this.Dimension.CreateBBFromPosition(this.Position);
    }

    /// <summary>
    /// Creates an entity of a plugin's entity type, of its kind, without spawning it.
    /// </summary>
    public static Entity Create(EntityDefinition definition, ILevel level) => definition.Kind == EntityKind.Mob
        ? new PluginMob(definition) { Level = level }
        : new PluginEntity(definition) { Level = level };
}
