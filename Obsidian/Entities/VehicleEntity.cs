using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Entities.AI;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Entities;

/// <summary>
/// What hurt a boat or a minecart, as far as vehicles care (vanilla's damage source tags they check).
/// </summary>
internal enum VehicleDamage
{
    /// <summary>A player or mob hitting it.</summary>
    Attack,

    /// <summary>A projectile that isn't burning.</summary>
    Projectile,

    /// <summary>A burning projectile, which ignites TNT minecarts.</summary>
    BurningProjectile,

    /// <summary>An explosion (vanilla's <c>is_explosion</c>).</summary>
    Explosion,

    /// <summary>Fire or lava (vanilla's <c>is_fire</c>).</summary>
    Fire,

    /// <summary>Anything else.</summary>
    Generic
}

/// <summary>
/// A boat or minecart, like vanilla's <c>VehicleEntity</c>: it shakes when hit, and breaks into its item once the
/// damage it took adds up past 40, every point of damage counting ten and wearing off by one a tick. Creative players
/// break it at once, without the item.
/// </summary>
/// <remarks>
/// Players use and hit it through <see cref="IPlayerInteractable"/>. It sends its own moves to clients each tick it
/// moved, with its motion, which vanilla's clients interpolate minecarts with.
/// </remarks>
public abstract class VehicleEntity : Entity, IPlayerInteractable
{
    // Vanilla VehicleEntity.hurtServer: a hit adds ten times its damage, and more than 40 breaks the vehicle.
    private const float DamageScale = 10;
    private const float BreakingDamage = 40;

    // Vanilla's hurt time after a hit, in ticks.
    private const int HurtDuration = 10;

    private MobTerrain? terrain;

    private int hurtTime;
    private int hurtDirection = 1;
    private float damage;

    private bool hasSentPosition;
    private VectorD lastSentPosition;
    private VectorD lastSentMotion;
    private Angle lastSentYaw;
    private Angle lastSentPitch;

    /// <summary>
    /// The moves its controlling player's client makes to it, for vehicles their clients predict (boats).
    /// </summary>
    internal ClientVehicleControl ClientControl { get; } = new();

    /// <summary>The blocks around it.</summary>
    internal MobTerrain Terrain => this.terrain ??= new MobTerrain(this.Level);

    /// <summary>Whether it's been broken or discarded.</summary>
    public bool IsRemoved { get; private set; }

    /// <summary>
    /// The ticks left of its shaking after a hit (vanilla's <c>DATA_ID_HURT</c>).
    /// </summary>
    public int HurtTime
    {
        get => this.hurtTime;
        set => this.SetData(ref this.hurtTime, value);
    }

    /// <summary>
    /// Which way it shakes, flipped each hit (vanilla's <c>DATA_ID_HURTDIR</c>).
    /// </summary>
    public int HurtDirection
    {
        get => this.hurtDirection;
        set => this.SetData(ref this.hurtDirection, value);
    }

    /// <summary>
    /// The damage it took that hasn't worn off yet (vanilla's <c>DATA_ID_DAMAGE</c>).
    /// </summary>
    public float Damage
    {
        get => this.damage;
        set => this.SetData(ref this.damage, value);
    }

    /// <summary>
    /// Whether it's in water, as of its last tick.
    /// </summary>
    internal bool InWater { get; private protected set; }

    /// <summary>
    /// Whether it's in lava, as of its last tick.
    /// </summary>
    internal bool InLava { get; private protected set; }

    /// <summary>
    /// Whether its last move was stopped horizontally by a block (vanilla's <c>horizontalCollision</c>).
    /// </summary>
    internal bool HorizontalCollision { get; private set; }

    /// <summary>
    /// The blocks it fell since it was last on the ground (vanilla's <c>fallDistance</c>).
    /// </summary>
    internal double FallDistance { get; set; }

    internal override void ResetFallDistance() => this.FallDistance = 0;

    /// <summary>
    /// How far it had fallen when it landed this tick, or 0 when it didn't land (what vanilla's
    /// <c>causeFallDamage</c> gets).
    /// </summary>
    private protected double LandedFrom { get; private set; }

    /// <summary>
    /// How far it falls a tick (vanilla's <c>getDefaultGravity</c>).
    /// </summary>
    internal abstract double Gravity { get; }

    /// <summary>
    /// The item it breaks into (vanilla's <c>getDropItem</c>).
    /// </summary>
    protected abstract Item DropItem { get; }

    /// <summary>
    /// The item a creative player picks from it (vanilla's <c>getPickResult</c>).
    /// </summary>
    internal virtual Item PickItem => this.DropItem;

    /// <summary>
    /// Where a passenger sits relative to the vehicle's position before the passenger's own attachment, unrotated
    /// (vanilla's passenger attachment point).
    /// </summary>
    protected abstract VectorD PassengerAttachment(int index, Entity passenger);

    /// <summary>
    /// Whether a source breaks it outright, whatever its damage (vanilla's <c>shouldSourceDestroy</c>).
    /// </summary>
    private protected virtual bool ShouldSourceDestroy(VehicleDamage source) => false;

    // Vanilla's getPassengerRidingPosition less the passenger's vehicle attachment point: a player sits 0.6 blocks
    // (scaled) below its seat.
    protected sealed override VectorD PassengerOffset(int index)
    {
        var passenger = this.Passengers[index];
        var seat = this.PassengerAttachment(index, passenger);
        var scale = passenger.HasAttribute("minecraft:scale")
            ? Math.Clamp(passenger.GetAttributeValue("minecraft:scale"), 0.0625f, 16f)
            : 1f;

        return seat - new VectorD(0, passenger is IPlayer ? 0.6 * scale : 0, 0);
    }

    // Vanilla's Vec3.yRot(-yaw in radians) of an attachment along the vehicle's length.
    private protected VectorD Rotated(double up, double forward)
    {
        var yaw = this.Yaw.Degrees * Math.PI / 180;
        return new VectorD(-Math.Sin(yaw) * forward, up, Math.Cos(yaw) * forward);
    }

    /// <summary>
    /// Vanilla's <c>hurtServer</c>: the vehicle shakes and takes the damage, breaking once it took enough, or at once
    /// when a creative player hit it.
    /// </summary>
    /// <param name="attacker">Who's behind the damage, if anyone.</param>
    /// <param name="amount">The damage, before the vehicle's scaling.</param>
    /// <param name="source">What kind of damage it is.</param>
    /// <param name="projectileMotion">The motion of the projectile that hit it, if one did.</param>
    internal async ValueTask HurtAsync(
        IEntity? attacker,
        float amount,
        VehicleDamage source,
        VectorD projectileMotion = default)
    {
        if (this.IsRemoved || !float.IsFinite(amount))
            return;

        await this.OnHurtAsync(attacker, source, projectileMotion);
        if (this.IsRemoved)
            return;

        this.HurtDirection = -this.HurtDirection;
        this.HurtTime = HurtDuration;
        this.Damage += amount * DamageScale;
        if (this.Level is AbstractLevel level)
            level.EmitGameEvent(MobGameEvent.EntityDamage, this.Position, attacker);

        var instabuild = attacker is IPlayer { GameMode: GameMode.Creative };
        if (!instabuild && this.Damage > BreakingDamage || this.ShouldSourceDestroy(source))
            await this.DestroyAsync(attacker, source);
        else if (instabuild)
            await this.DiscardAsync();

        this.SynchronizeData();
    }

    /// <summary>
    /// Runs before a hit lands, like the start of vanilla's <c>hurtServer</c> overrides (a burning arrow lighting a TNT
    /// minecart).
    /// </summary>
    private protected virtual ValueTask OnHurtAsync(IEntity? attacker, VehicleDamage source, VectorD projectileMotion) =>
        default;

    /// <summary>
    /// Vanilla's <c>destroy(ServerLevel, DamageSource)</c>: the vehicle breaks into its item.
    /// </summary>
    private protected virtual ValueTask DestroyAsync(IEntity? attacker, VehicleDamage source) =>
        this.DestroyAsync(this.DropItem);

    /// <summary>
    /// Vanilla's <c>destroy(ServerLevel, Item)</c>: the vehicle goes, and drops <paramref name="item"/>, named like the
    /// vehicle, where <c>entity_drops</c> is on.
    /// </summary>
    private protected async ValueTask DestroyAsync(Item item)
    {
        await this.DiscardAsync();

        if (this.Level is not AbstractLevel level || !level.LevelData.GetBooleanRule("entity_drops"))
            return;

        var stack = new ItemStack(item);
        if (this.CustomName is ChatMessage name)
            stack[DataComponentType.CustomName] = ComponentBuilder.CustomName with { Value = name };

        level.SpawnItem(this.Position, stack);
    }

    /// <summary>
    /// Takes the vehicle out of the level for good, with its passengers off and whatever it carries dropped (vanilla's
    /// <c>remove</c> with a reason that destroys it).
    /// </summary>
    internal async ValueTask DiscardAsync()
    {
        if (this.IsRemoved)
            return;

        this.IsRemoved = true;
        this.EjectPassengers();
        await this.OnDiscardedAsync();
        await this.RemoveAsync();
    }

    /// <summary>
    /// Runs as the vehicle is broken or discarded, before it leaves the level: containers drop their items.
    /// </summary>
    private protected virtual ValueTask OnDiscardedAsync() => default;

    // Vanilla's /kill: like breaking it, without its item.
    public override ValueTask KillAsync(IEntity source) => this.DiscardAsync();

    public override ValueTask KillAsync(IEntity source, ChatMessage message) => this.DiscardAsync();

    public override ValueTask DamageAsync(IEntity source, float amount = 1) =>
        this.HurtAsync(source, amount, VehicleDamage.Attack);

    /// <summary>
    /// Seats <paramref name="passenger"/>, if the vehicle has room for it (vanilla's <c>startRiding</c> with
    /// <c>canAddPassenger</c>).
    /// </summary>
    internal bool Board(Entity passenger)
    {
        if (this.IsRemoved
            || passenger is IPlayer { GameMode: GameMode.Spectator }
            || passenger is Living { Alive: false }
            || !this.CanAddPassenger(passenger))
        {
            return false;
        }

        return passenger.Ride(this);
    }

    /// <summary>
    /// Whether another passenger fits (vanilla's <c>canAddPassenger</c>).
    /// </summary>
    protected virtual bool CanAddPassenger(Entity passenger) => this.Passengers.Count < this.MaximumPassengers;

    /// <inheritdoc/>
    public virtual ValueTask<bool> InteractAsync(PlayerBase player, InteractionHand hand) => ValueTask.FromResult(false);

    /// <inheritdoc/>
    public async ValueTask AttackedAsync(PlayerBase player)
    {
        var (damage, _) = player.GetAttackDamage(this);
        player.ResetAttackStrength();

        await this.HurtAsync(player, damage, VehicleDamage.Attack);
    }

    /// <summary>
    /// The start of every vehicle's tick, like vanilla's <c>VehicleEntity</c> subclasses': the shaking and damage
    /// wear off, passengers that can't stay get off, and lava burns it.
    /// </summary>
    private protected async ValueTask<bool> TickVehicleAsync()
    {
        if (this.IsRemoved)
            return false;

        this.LandedFrom = 0;
        if (this.HurtTime > 0)
            this.HurtTime--;
        if (this.Damage > 0)
            this.Damage--;

        this.DropInvalidPassengers();

        // Vanilla's checkBelowWorld: 64 blocks under the level, it's gone.
        if (this.Level is AbstractLevel bounded && this.Position.Y < bounded.MinY - 64)
        {
            await this.DiscardAsync();
            return false;
        }

        var bounds = this.Dimension.CreateBBFromPosition(this.Position);
        this.InWater = this.Terrain.ContainsFluid(bounds, water: true);
        this.InLava = this.Terrain.ContainsFluid(bounds, water: false);

        // Vanilla's lavaHurt: four damage a tick, which breaks a vehicle in two.
        if (this.InLava)
        {
            this.Burning = true;
            await this.HurtAsync(null, 4, VehicleDamage.Fire);
            if (this.IsRemoved)
                return false;
        }

        return true;
    }

    // Passengers get off when they die, leave or change level; players also by sneaking (vanilla's
    // ServerPlayer.wantsToStopRiding).
    private void DropInvalidPassengers()
    {
        foreach (var passenger in this.Passengers.ToArray())
        {
            if (passenger.Level != this.Level
                || !passenger.IsActive
                || passenger is IPlayer && passenger.Sneaking)
            {
                passenger.LeaveVehicle();
            }
        }
    }

    /// <summary>
    /// Vanilla's <c>Entity.move</c> for a vehicle: moves it by <paramref name="delta"/> as far as blocks let it,
    /// recording where it was stopped, whether it's on the ground, and how far it fell.
    /// </summary>
    /// <returns>Where it ended up, which <see cref="Position"/> is set to.</returns>
    private protected VectorD Move(VectorD delta, bool stopOnCollision = true)
    {
        var bounds = this.Dimension.CreateBBFromPosition(this.Position);
        var onGround = this.MovementFlags.HasFlag(MovementFlags.OnGround);
        var displacement = EntityMovement.Collide(this.Terrain, bounds, delta, 0, onGround);

        var collidedX = Math.Abs(displacement.X - delta.X) > 1.0E-5;
        var collidedY = Math.Abs(displacement.Y - delta.Y) > 1.0E-5;
        var collidedZ = Math.Abs(displacement.Z - delta.Z) > 1.0E-5;

        this.HorizontalCollision = collidedX || collidedZ;
        var landed = collidedY && delta.Y < 0;
        this.MovementFlags = landed ? MovementFlags.OnGround : MovementFlags.None;

        if (stopOnCollision)
        {
            var motion = this.Motion;
            this.Motion = new VectorD(collidedX ? 0 : motion.X, collidedY ? 0 : motion.Y, collidedZ ? 0 : motion.Z);
        }

        if (landed)
        {
            this.LandedFrom = this.FallDistance;
            this.FallDistance = 0;
        }
        else if (displacement.Y < 0)
            this.FallDistance -= displacement.Y;

        var next = this.Position + displacement;
        this.SetPosition(next);
        return next;
    }

    /// <summary>
    /// Moves the vehicle to <paramref name="position"/> within its level, if the level can hold it there.
    /// </summary>
    private protected bool SetPosition(VectorD position)
    {
        if (position == this.Position)
            return true;

        if (this.Level is AbstractLevel level && !level.TryMoveEntity(this, this.Position, position))
        {
            this.Motion = VectorD.Zero;
            return false;
        }

        this.Position = position;
        this.BoundingBox = this.Dimension.CreateBBFromPosition(position);
        return true;
    }

    /// <summary>
    /// The end of every vehicle's tick: its passengers go to their seats, and clients hear of how it moved and of its
    /// synced data.
    /// </summary>
    private protected void FinishTick(VectorD oldPosition)
    {
        this.LastPosition = oldPosition;
        this.PositionPassengers();
        this.SynchronizeMovement(oldPosition);
        this.SynchronizeData();
    }

    /// <summary>
    /// Sends its synced data to the clients that see it, if it changed.
    /// </summary>
    private protected void SynchronizeData()
    {
        if (!this.DataChanged)
            return;

        this.DataChanged = false;
        this.PacketBroadcaster.QueuePacketToLevelInRange(this.Level, this.Position, new SetEntityDataPacket
        {
            EntityId = this.EntityId,
            Entity = this
        });
    }

    private void SynchronizeMovement(VectorD oldPosition)
    {
        if (!this.hasSentPosition)
        {
            this.lastSentPosition = oldPosition;
            this.hasSentPosition = true;
        }

        var onGround = this.MovementFlags.HasFlag(MovementFlags.OnGround);
        var delta = MoveDelta(this.lastSentPosition, this.Position);
        var moved = delta != Vector.Zero;
        var rotated = this.Yaw != this.lastSentYaw || this.Pitch != this.lastSentPitch;

        if (moved && !IsRelativeMove(delta))
        {
            this.PacketBroadcaster.BroadcastToLevelInRange(this.Level, this.Position, new TeleportEntityPacket
            {
                EntityId = this.EntityId,
                Position = this.Position,
                Delta = this.Motion,
                Yaw = this.Yaw,
                Pitch = this.Pitch,
                OnGround = onGround
            }, this.EntityId);
            this.lastSentPosition = this.Position;
        }
        else if (moved)
        {
            this.PacketBroadcaster.BroadcastToLevelInRange(this.Level, this.Position, new MoveEntityPosRotPacket
            {
                EntityId = this.EntityId,
                Delta = delta,
                Yaw = this.Yaw,
                Pitch = this.Pitch,
                OnGround = onGround
            }, this.EntityId);
            this.lastSentPosition = this.Position;
        }
        else if (rotated)
        {
            this.PacketBroadcaster.BroadcastToLevelInRange(this.Level, this.Position, new MoveEntityRotPacket
            {
                EntityId = this.EntityId,
                Yaw = this.Yaw,
                Pitch = this.Pitch,
                OnGround = onGround
            }, this.EntityId);
        }

        this.lastSentYaw = this.Yaw;
        this.lastSentPitch = this.Pitch;

        // Like vanilla's ServerEntity for entities that track their deltas: the motion follows when it changed.
        if ((this.Motion - this.lastSentMotion).MagnitudeSquared() > 1.0E-7
            || this.Motion == VectorD.Zero && this.lastSentMotion != VectorD.Zero)
        {
            this.lastSentMotion = this.Motion;
            this.PacketBroadcaster.BroadcastToLevelInRange(this.Level, this.Position, new SetEntityMotionPacket
            {
                EntityId = this.EntityId,
                Velocity = new Velocity(this.Motion.X, this.Motion.Y, this.Motion.Z)
            }, this.EntityId);
        }
    }

    /// <summary>
    /// Vanilla's <c>Entity.push(Entity)</c>: two entities that overlap push each other apart, a vehicle with
    /// passengers excepted. Players move themselves, so only the vehicle is pushed by one.
    /// </summary>
    private protected static void PushApart(Entity first, Entity second)
    {
        var dx = second.Position.X - first.Position.X;
        var dz = second.Position.Z - first.Position.Z;
        var distance = Math.Max(Math.Abs(dx), Math.Abs(dz));
        if (distance < 0.01)
            return;

        distance = Math.Sqrt(distance);
        dx /= distance;
        dz /= distance;
        var scale = Math.Min(1, 1 / distance);
        dx *= scale * 0.05;
        dz *= scale * 0.05;

        if (first.Passengers.Count == 0 && first is not IPlayer)
            first.Motion += new VectorD(-dx, 0, -dz);
        if (second.Passengers.Count == 0 && second is not IPlayer)
            second.Motion += new VectorD(dx, 0, dz);
    }

    /// <summary>
    /// The entities a vehicle may push or be pushed by, like vanilla's <c>EntitySelector.pushableBy</c>: living
    /// things, boats and minecarts, but not its own passengers, spectators or what rides the same vehicle.
    /// </summary>
    private protected IEnumerable<Entity> PushableEntitiesIn(BoundingBox bounds) => this.Level
        .GetEntitiesInRange(this.Position, (float)Math.Max(bounds.Width, bounds.Depth) + 4)
        .OfType<Entity>()
        .Where(entity => !ReferenceEquals(entity, this)
            && entity is Living or VehicleEntity
            && entity.IsActive
            && entity is not VehicleEntity { IsRemoved: true }
            && entity.Vehicle != this
            && (this.Vehicle is null || entity.Vehicle != this.Vehicle)
            && MobTerrain.Overlaps(bounds, entity.Dimension.CreateBBFromPosition(entity.Position)));

    private void SetData<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        this.DataChanged = true;
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteEntityMetadataType(8, EntityMetadataType.VarInt);
        writer.WriteVarInt(this.HurtTime);

        writer.WriteEntityMetadataType(9, EntityMetadataType.VarInt);
        writer.WriteVarInt(this.HurtDirection);

        writer.WriteEntityMetadataType(10, EntityMetadataType.Float);
        writer.WriteSingle(this.Damage);

        this.MetadataIndex = 11;
    }
}
