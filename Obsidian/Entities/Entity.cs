using Obsidian.API.AI;
using Obsidian.API.World;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Obsidian.Entities;

public partial class Entity : IEquatable<Entity>, IEntity
{
    // Retained for derived classes; built-in serializers use fixed indexes to avoid concurrent-write races.
    protected byte MetadataIndex { get; set; }

    public required ILevel Level { get; set; }

    public IPacketBroadcaster PacketBroadcaster => this.Level.PacketBroadcaster;
    public IEventDispatcher EventDispatcher => this.Level.EventDispatcher;

    #region Location properties
    public VectorD LastPosition { get; set; }

    public VectorD Position
    {
        get => this.position;
        set
        {
            this.position = value;

            // Racy outside the grid's lock, but Move checks again under it.
            if (this.Grid is EntityGrid grid && EntityGrid.ChunkKey(value) != this.GridChunk)
                grid.Move(this, value);
        }
    }

    private VectorD position;

    // The level's grid the entity is in and the chunk it's filed under there, both kept by the grid (see EntityGrid).
    internal EntityGrid? Grid { get; set; }
    internal long GridChunk { get; set; }

    public Angle Pitch { get; set; }

    public Angle Yaw { get; set; }

    /// <summary>
    /// The entity's velocity in blocks per tick, saved as vanilla's <c>Motion</c>.
    /// </summary>
    public VectorD Motion { get; set; }
    #endregion Location properties

    // The distance walked or swum since the last step or swim game event (see AbstractLevel.EmitMovementGameEventsAsync).
    internal double GameEventStepDistance { get; set; }

    public int EntityId { get; internal set; }

    /// <summary>
    /// The entity's UUID: a random one, unless it's given one (by its constructor, an initializer or its save) before
    /// it joins a level. It's given at most one: a level finds its entities by it (see <see cref="EntityGrid"/>), and
    /// a second, different one throws.
    /// </summary>
    /// <exception cref="InvalidOperationException">The entity was given another UUID already, or is in a level.</exception>
    public Guid Uuid
    {
        get => this.uuid;
        internal set
        {
            if (value == this.uuid)
                return;

            if (this.uuidAssigned || this.Grid is not null)
                throw new InvalidOperationException($"Entity {this.EntityId} already has UUID {this.uuid}; an entity's UUID is set once.");

            this.uuid = value;
            this.uuidAssigned = true;
        }
    }

    private Guid uuid = Guid.NewGuid();
    private bool uuidAssigned;

    // Whether ReadNbt takes the UUID a save holds; a player keeps their profile's.
    private protected virtual bool ReadsSavedUuid => true;

    public Pose Pose { get; set; } = Pose.Standing;

    public virtual BoundingBox BoundingBox { get; protected set; } = new(VectorD.Zero, VectorD.Zero);
    public virtual EntityDimension Dimension { get; protected set; } = EntityDimension.Zero;
    protected virtual float DimensionScale => 1;

    // LivingEntity.getScale clamps the attribute; age/type scaling remains independent. It's worked out again only
    // once the scale attribute changes (see OnAttributeChanged), since dimensions are read many times a tick by
    // collisions and pathfinding.
    internal float AttributeScale
    {
        get
        {
            var revision = Volatile.Read(ref this.attributeScaleRevision);
            if (Volatile.Read(ref this.attributeScale) is ScaleReading reading && reading.Revision == revision)
                return reading.Scale;

            var scale = this.HasAttribute("minecraft:scale")
                ? Math.Clamp(this.GetAttributeValue("minecraft:scale"), 0.0625f, 16f)
                : 1;

            // Kept only if the attribute didn't change while it was read, so an older reading can't replace a newer.
            lock (this.attributeLock)
            {
                if (this.attributeScaleRevision == revision)
                    this.attributeScale = new ScaleReading(scale, revision);
            }

            return scale;
        }
    }

    private ScaleReading? attributeScale;

    // Counts the scale attribute's changes; a reading of another count is out of date.
    private int attributeScaleRevision;

    private sealed record ScaleReading(float Scale, int Revision);

    // Each scale is read once, and multiplied in the same order as before, so the dimensions round the same.
    protected EntityDimension ScaleDimensions(float width, float height)
    {
        var dimensionScale = this.DimensionScale;
        var attributeScale = this.AttributeScale;
        return new()
        {
            Width = width * dimensionScale * attributeScale,
            Height = height * dimensionScale * attributeScale
        };
    }

    public int PowderedSnowTicks { get; set; }

    public EntityType? Type { get; set; }

    public virtual API.Entities.EntityDefinition? Definition => null;

    /// <summary>
    /// The plugin whose code spawned the entity, which unloading the plugin removes it for, or null for the server's
    /// (see <see cref="Plugins.PluginEntities"/>).
    /// </summary>
    internal Plugins.PluginContainer? SpawnedBy { get; set; }

    /// <summary>
    /// The entity's type's id in the entity type registry, which clients are sent: its vanilla type's, or its plugin
    /// type's.
    /// </summary>
    internal int TypeId => this.Definition?.Id
        ?? (int)(this.Type ?? throw new InvalidOperationException($"Entity {this.EntityId} has no type."));

    /// <summary>
    /// Drops what an unloading plugin, whose code is in <paramref name="context"/>, gave the entity through the API: a
    /// navigator or goal controller, which would keep the plugin loaded. Run on the entity's level's tick.
    /// </summary>
    internal virtual void ReleasePluginObjects(System.Runtime.Loader.AssemblyLoadContext context)
    {
        if (this.GoalController is AI.GoalSelector selector)
            selector.RemoveGoalsFrom(context);
        else if (IsFrom(this.GoalController, context))
            this.GoalController = null;

        if (IsFrom(this.Navigator, context))
            this.Navigator = null;
    }

    /// <summary>
    /// Whether an object's code is in <paramref name="context"/>.
    /// </summary>
    internal static bool IsFrom(object? value, System.Runtime.Loader.AssemblyLoadContext context) =>
        value is not null
        && System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(value.GetType().Assembly) == context;

    /// <summary>
    /// Whether an entity's type, vanilla's or a plugin's, is in an entity type tag.
    /// </summary>
    internal static bool IsIn(IEntity entity, Tag tag) => entity.Definition?.Id is int id
        ? tag.Entries.Contains(id)
        : entity.Type is EntityType type && tag.Entries.Contains((int)type);

    public short Air { get; set; } = 300;

    public float Health { get; set; } = 100;

    public ChatMessage? CustomName { get; set; }

    public virtual string? TranslationKey { get; protected set; }

    public virtual bool CustomNameVisible { get; set; }
    public virtual bool Silent { get; set; }
    public virtual bool NoGravity { get; set; }
    public virtual MovementFlags MovementFlags { get; set; }
    public virtual bool Sneaking { get; set; }
    public virtual bool Sprinting { get; set; }
    public virtual bool CanBeSeen { get; set; }//What does this do???
    public virtual bool Glowing { get; set; }
    public virtual bool Invisible { get; set; }
    public virtual bool Burning { get; set; }
    public virtual bool Swimming { get; set; }
    public virtual bool FlyingWithElytra { get; set; }

    public virtual bool Summonable { get; set; }

    public virtual bool IsFireImmune { get; set; }

    public INavigator? Navigator { get; set; }
    public IGoalController? GoalController { get; set; }

    /// <summary>
    /// Saved fields Obsidian doesn't model (e.g. a shulker's <c>Color</c> or a villager's <c>VillagerData</c>), kept as
    /// they were loaded so saving the entity again doesn't drop them.
    /// </summary>
    internal NbtCompound UnmodeledData { get; set; } = new();

    #region NBT
    /// <summary>
    /// Writes the fields vanilla's <c>Entity.saveWithoutId</c> saves that this entity models into <paramref name="tag"/>,
    /// replacing what's there. Overrides add their own fields.
    /// </summary>
    /// <remarks>
    /// <paramref name="tag"/> starts with <see cref="UnmodeledData"/>, so fields that are only saved when set are removed
    /// when they aren't.
    /// </remarks>
    internal virtual void WriteNbt(NbtCompound tag)
    {
        API.Data.AttachmentStorage.For(this).Save(tag);
        tag.Set(EntityNbt.DoubleList("Pos", this.Position));
        tag.Set(EntityNbt.DoubleList("Motion", this.Motion));
        tag.Set(new NbtList(NbtTagType.Float, "Rotation")
        {
            new NbtTag<float>(string.Empty, this.Yaw.Degrees),
            new NbtTag<float>(string.Empty, this.Pitch.Degrees)
        });
        tag.Set(new NbtTag<short>("Air", this.Air));
        tag.Set(new NbtTag<bool>("OnGround", this.MovementFlags.HasFlag(MovementFlags.OnGround)));
        tag.Set(new NbtTag<int>("TicksFrozen", PowderedSnowTicks));
        tag.Set(new NbtArray<int>("UUID", EntityNbt.UuidToInts(this.Uuid)));
        tag.SetOrRemove("CustomName", this.CustomName?.ToNbt("CustomName"));

        // Like vanilla, these flags are only saved when set.
        tag.SetFlag("CustomNameVisible", this.CustomNameVisible);
        tag.SetFlag("Silent", this.Silent);
        tag.SetFlag("NoGravity", this.NoGravity);
        tag.SetFlag("Glowing", this.Glowing);
    }

    /// <summary>
    /// Reads the fields <see cref="WriteNbt"/> writes, keeping the defaults of missing ones.
    /// </summary>
    internal virtual void ReadNbt(NbtCompound tag)
    {
        API.Data.AttachmentStorage.For(this).Load(tag);
        if (EntityNbt.TryReadVector(tag, "Pos", out var position))
            this.Position = position;
        if (EntityNbt.TryReadVector(tag, "Motion", out var motion))
            this.Motion = motion;

        if (tag.TryGetTag<NbtList>("Rotation", out var rotation) && rotation.Count >= 2
            && rotation[0] is NbtTag<float> yaw && rotation[1] is NbtTag<float> pitch)
        {
            this.Yaw = yaw.Value;
            this.Pitch = pitch.Value;
        }

        if (tag.TryGetTag<NbtTag<short>>("Air", out var air))
            this.Air = air.Value;
        if (tag.TryGetBool("OnGround", out var onGround) && onGround)
            this.MovementFlags |= MovementFlags.OnGround;
        if (tag.TryGetTagValue<int>("TicksFrozen", out var frozen)) PowderedSnowTicks = Math.Clamp(frozen, 0, 140);
        if (this.ReadsSavedUuid && tag.TryGetTag<NbtArray<int>>("UUID", out var uuid) && uuid.Count == 4)
            this.Uuid = EntityNbt.UuidFromInts(uuid.GetArray());
        if (tag.TryGetTag("CustomName", out var customName))
            this.CustomName = customName.TextFromNbt();

        this.CustomNameVisible = tag.TryGetBool("CustomNameVisible", out var nameVisible) && nameVisible;
        this.Silent = tag.TryGetBool("Silent", out var silent) && silent;
        this.NoGravity = tag.TryGetBool("NoGravity", out var noGravity) && noGravity;
        this.Glowing = tag.TryGetBool("Glowing", out var glowing) && glowing;
    }
    #endregion NBT

    #region Update methods
    /// <summary>
    /// A move as a relative-move packet's delta, in 1/4096 blocks: each position rounded to that scale, then
    /// subtracted. This matches vanilla's <c>VecDeltaCodec</c> (Java's <c>Math.round</c>, so halves round up),
    /// which the client decodes the delta with; rounding the difference instead lets repeated moves drift.
    /// </summary>
    internal static Vector MoveDelta(VectorD from, VectorD to) =>
        new(MoveDelta(from.X, to.X), MoveDelta(from.Y, to.Y), MoveDelta(from.Z, to.Z));

    // Clamped rather than cast, so a move of 2^32 units or more can't wrap into a small delta that passes IsRelativeMove.
    private static int MoveDelta(double from, double to) =>
        (int)Math.Clamp(JavaRound(to * 4096) - JavaRound(from * 4096), int.MinValue, int.MaxValue);

    /// <summary>
    /// Java's <c>Math.round</c>: to the nearest whole number, halves up. Adding 0.5 and flooring would round the
    /// sum first (0.49999999999999994 + 0.5 is 1); a number minus its floor is exact, so its fraction compares safely.
    /// </summary>
    private static long JavaRound(double value)
    {
        var floor = Math.Floor(value);
        return (long)floor + (value - floor >= 0.5 ? 1 : 0);
    }

    /// <summary>
    /// Whether a move fits a relative-move packet, whose delta is a short on each axis; vanilla sends larger moves
    /// as a teleport to the absolute position.
    /// </summary>
    internal static bool IsRelativeMove(Vector delta) =>
        delta.X is >= short.MinValue and <= short.MaxValue
        && delta.Y is >= short.MinValue and <= short.MaxValue
        && delta.Z is >= short.MinValue and <= short.MaxValue;

    private void BroadcastTeleport(VectorD position, Angle yaw, Angle pitch, MovementFlags movementFlags) =>
        this.PacketBroadcaster.BroadcastToLevelInRange(this.Level, position, new TeleportEntityPacket
        {
            EntityId = EntityId,
            OnGround = movementFlags.HasFlag(MovementFlags.OnGround),
            Position = position,
            Pitch = pitch,
            Yaw = yaw
        }, EntityId);

    public virtual async ValueTask UpdateAsync(VectorD position, MovementFlags movementFlags)
    {
        // Moved when the move shows on the client: when its delta, in 1/4096 blocks, isn't zero.
        var delta = MoveDelta(Position, position);
        var isNewLocation = delta != Vector.Zero;

        if (!IsRelativeMove(delta))
        {
            this.BroadcastTeleport(position, Yaw, Pitch, movementFlags);
        }
        else if (isNewLocation)
        {
            this.PacketBroadcaster.BroadcastToLevelInRange(this.Level, position, new MoveEntityPosPacket
            {
                EntityId = EntityId,

                Delta = delta,

                OnGround = movementFlags.HasFlag(MovementFlags.OnGround)
            }, EntityId);
        }

        await UpdatePositionAsync(position, movementFlags);
    }

    public virtual async ValueTask UpdateAsync(VectorD position, Angle yaw, Angle pitch, MovementFlags movementFlags)
    {
        // Moved when the move shows on the client: when its delta, in 1/4096 blocks, isn't zero.
        var delta = MoveDelta(Position, position);
        var isNewLocation = delta != Vector.Zero;
        var isNewRotation = yaw != Yaw || pitch != Pitch;

        if (!IsRelativeMove(delta))
        {
            this.BroadcastTeleport(position, yaw, pitch, movementFlags);
            if (isNewRotation)
                this.SetHeadRotation(yaw);
        }
        else if (isNewLocation)
        {
            if (isNewRotation)
            {
                this.PacketBroadcaster.BroadcastToLevelInRange(this.Level, position, new MoveEntityPosRotPacket
                {
                    EntityId = EntityId,

                    Delta = delta,

                    Yaw = yaw,
                    Pitch = pitch,

                    OnGround = movementFlags.HasFlag(MovementFlags.OnGround)
                }, EntityId);

                this.SetHeadRotation(yaw);
            }
            else
            {
                this.PacketBroadcaster.BroadcastToLevelInRange(this.Level, position, new MoveEntityPosPacket
                {
                    EntityId = EntityId,

                    Delta = delta,

                    OnGround = movementFlags.HasFlag(MovementFlags.OnGround)
                }, EntityId);
            }
        }
        else if (isNewRotation)
        {
            // Turned without moving.
            this.SetRotation(yaw, pitch, movementFlags);
            this.SetHeadRotation(yaw);
        }

        await UpdatePositionAsync(position, yaw, pitch, movementFlags);
    }

    public virtual ValueTask UpdateAsync(Angle yaw, Angle pitch, MovementFlags movementFlags)
    {
        var isNewRotation = yaw != Yaw || pitch != Pitch;

        if (isNewRotation)
        {
            this.SetRotation(yaw, pitch, movementFlags);
            this.SetHeadRotation(yaw);
        }

        return default;
    }

    public bool IsInRange(IEntity entity, float distance)
    {
        if (this.Level != entity.Level)
            return false;

        var locationDifference = LocationDiff.GetDifference(this.Position, entity.Position);

        distance *= distance;

        return locationDifference.CalculatedDifference <= distance;
    }


    public void SetHeadRotation(Angle headYaw) =>
        this.PacketBroadcaster.BroadcastToLevelInRange(this.Level, this.Position, new RotateHeadPacket
        {
            EntityId = EntityId,
            HeadYaw = headYaw
        }, EntityId);

    public void SetRotation(Angle yaw, Angle pitch, MovementFlags movementFlags)
    {
        this.PacketBroadcaster.BroadcastToLevelInRange(this.Level, this.Position, new MoveEntityRotPacket
        {
            EntityId = EntityId,
            OnGround = movementFlags.HasFlag(MovementFlags.OnGround),
            Yaw = yaw,
            Pitch = pitch
        }, EntityId);

        this.UpdatePosition(yaw, pitch, movementFlags);
    }

    public async Task UpdatePositionAsync(VectorD pos, MovementFlags movementFlags)
    {
        var (x, z) = WorldData.Region.ChunkOf(pos);
        var chunk = await this.Level.GetChunkAsync(x, z, false);
        if (chunk is not null && chunk.IsGenerated)
        {
            Position = pos;
        }

        MovementFlags = movementFlags;

        if (Dimension != EntityDimension.Zero)
            BoundingBox = Dimension.CreateBBFromPosition(pos);
    }

    public async Task UpdatePositionAsync(VectorD pos, Angle yaw, Angle pitch, MovementFlags movementFlags = MovementFlags.OnGround)
    {
        var (x, z) = WorldData.Region.ChunkOf(pos);
        var chunk = await Level.GetChunkAsync(x, z, false);
        if (chunk is { IsGenerated: true })
        {
            Position = pos;
        }

        Yaw = yaw;
        Pitch = pitch;
        MovementFlags = movementFlags;

        if (Dimension != EntityDimension.Zero)
            BoundingBox = Dimension.CreateBBFromPosition(pos);
    }

    public void UpdatePosition(Angle yaw, Angle pitch, MovementFlags movementFlags = MovementFlags.OnGround)
    {
        Yaw = yaw;
        Pitch = pitch;
        MovementFlags = movementFlags;
    }
    #endregion

    public VectorF GetLookDirection()
    {
        const float DegreesToRadian = MathF.PI / 180f;
        float pitch = Pitch.Degrees * DegreesToRadian;
        float yaw = Yaw.Degrees * DegreesToRadian;

        (float sinPitch, float cosPitch) = MathF.SinCos(pitch);
        (float sinYaw, float cosYaw) = MathF.SinCos(yaw);
        return new(-cosPitch * sinYaw, -sinPitch, cosPitch * cosYaw);
    }

    public virtual async ValueTask RemoveAsync()
    {
        ValueTask<bool> removal;
        lock (ItemEntity.TransferLock)
            removal = this.Level.DestroyEntityAsync(this);

        await removal;
    }

    protected virtual EntityBitMask GenerateBitmask()
    {
        // Update pose
        if (Sneaking)
            Pose = Pose.Sneaking;
        else if (Swimming)
            Pose = Pose.Swimming;
        else if (!Sneaking && Pose == Pose.Sneaking || !Swimming && Pose == Pose.Swimming)
            Pose = Pose.Standing;

        // Collect flags
        EntityBitMask mask = EntityBitMask.None;
        
        if (Sneaking)
            mask |= EntityBitMask.Crouched;
        if (Swimming)
            mask |= EntityBitMask.Swimming;
        if (Sprinting)
            mask |= EntityBitMask.Sprinting;
        if (Glowing)
            mask |= EntityBitMask.Glowing;
        if (Invisible)
            mask |= EntityBitMask.Invisible;
        if (Burning)
            mask |= EntityBitMask.OnFire;
        if (FlyingWithElytra)
            mask |= EntityBitMask.FlyingWithElytra;

        return mask;
    }

    public virtual void Write(INetStreamWriter writer)
    {
        writer.WriteEntityMetadataType(0, EntityMetadataType.Byte);
        writer.WriteByte(GenerateBitmask());

        writer.WriteEntityMetadataType(1, EntityMetadataType.VarInt);
        writer.WriteVarInt(Air);

        writer.WriteEntityMetadataType(2, EntityMetadataType.OptionalTextComponent);
        writer.WriteOptional(CustomName);

        writer.WriteEntityMetadataType(3, EntityMetadataType.Boolean);
        writer.WriteBoolean(CustomNameVisible);

        writer.WriteEntityMetadataType(4, EntityMetadataType.Boolean);
        writer.WriteBoolean(Silent);

        writer.WriteEntityMetadataType(5, EntityMetadataType.Boolean);
        writer.WriteBoolean(NoGravity);

        writer.WriteEntityMetadataType(6, EntityMetadataType.Pose);
        writer.WriteVarInt(this.Pose);

        writer.WriteEntityMetadataType(7, EntityMetadataType.VarInt);
        writer.WriteVarInt(PowderedSnowTicks);

        this.MetadataIndex = 8;
    }

    protected void WriteEntityMetadataType(INetStreamWriter writer, EntityMetadataType type) =>
        writer.WriteEntityMetadataType(this.MetadataIndex++, type);

    public IEnumerable<IEntity> GetEntitiesNear(float distance) => Level.GetEntitiesInRange(Position, distance).Where(x => !ReferenceEquals(x, this));

    //TODO GRAVITY
    public virtual ValueTask TickAsync() => default;

    //TODO check for other entities and handle accordingly 
    public virtual async ValueTask DamageAsync(IEntity source, float amount = 1.0f)
    {
        if (!float.IsFinite(amount) || amount <= 0 || Health <= 0 || source.Level != Level ||
            this is IPlayer immune && immune.GameMode is GameMode.Creative or GameMode.Spectator)
            return;

        Health -= amount;
        if (Level is Obsidian.WorldData.AbstractLevel events)
        {
            events.EmitGameEvent(Obsidian.WorldData.MobGameEvent.EntityDamage, Position, this);
            if (Health <= 0) events.EmitGameEvent(Obsidian.WorldData.MobGameEvent.EntityDie, Position, this);
        }
        if (this is IPlayer attackedPlayer && !ReferenceEquals(source, this))
            Wolf.AlertOwnedWolves(attackedPlayer, source);

        if (this is ILiving living)
        {
            this.PacketBroadcaster.QueuePacketToLevel(this.Level, new HurtAnimationPacket
            {
                EntityId = EntityId,
                Yaw = (float)(Math.Atan2(source.Position.Z - Position.Z, source.Position.X - Position.X) * 180 / Math.PI - Yaw.Degrees)
            });

            if (living is IPlayer player)
            {
                player.AddExhaustion(0.1f);
                await player.QueuePacketAsync(new SetHealthPacket(Math.Max(0, Health), player.FoodLevel, player.FoodSaturationLevel));

                if (!player.Alive)
                    await player.KillAsync(source, ChatMessage.Simple("You died xd"));
            }
        }
    }

    public virtual ValueTask KillAsync(IEntity source) => default;
    public virtual ValueTask KillAsync(IEntity source, ChatMessage message) => default;

    public bool Equals([AllowNull] Entity other)
    {
        if (other is null)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        return EntityId == other.EntityId;
    }

    public override bool Equals(object? obj) => Equals(obj as Entity);

    public static implicit operator int(Entity entity) => entity.EntityId;

    public static bool operator ==(Entity? a, Entity? b)
    {
        if (ReferenceEquals(a, b))
            return true;

        return a is not null && a.Equals(b);
    }

    public static bool operator !=(Entity? a, Entity? b) => !(a == b);

    public override int GetHashCode() => EntityId.GetHashCode();

    public virtual ValueTask TeleportAsync(IWorld world) =>
        this.TeleportAsync(world, world.LevelData.SpawnPosition);

    public virtual ValueTask TeleportAsync(IEntity to) =>
        this.TeleportAsync(to.Level, to.Position, to.Yaw, to.Pitch);

    public virtual ValueTask TeleportAsync(VectorD pos) => this.TeleportAsync(this.Level, pos);

    public virtual void SpawnEntity(Velocity? velocity = null, int additionalData = 0)
    {
        var packet = CreateSpawnPacket(velocity, additionalData);
        var range = Level is Obsidian.WorldData.AbstractLevel level ? level.Configuration.EntityBroadcastRangePercentage : 100;
        foreach (var player in Level.GetPlayersInRange(Position, range))
        {
            if (player.EntityId == EntityId)
                continue;
            var (x, z) = Position.ToChunkCoord();
            if (this is not IPlayer && !player.LoadedChunks.Contains(NumericsHelper.IntsToLong(x, z)))
                continue;
            PacketBroadcaster.QueuePacketTo(packet, ids: [player.EntityId]);
            if (this is not IPlayer)
                player.TrackEntity(this);
        }
    }

    /// <summary>
    /// Where a client that starts seeing the entity is told it is: the base its relative moves are sent from, which
    /// for a mob is the position it last sent (see <see cref="Mob.PairingPosition"/>).
    /// </summary>
    private protected virtual VectorD PairingPosition => this.Position;

    internal virtual IClientboundPacket CreateSpawnPacket(Velocity? velocity = null, int additionalData = 0)
    {
        List<Net.Packets.ClientboundPacket> packets =
        [
            new AddEntityPacket
            {
                EntityId = this.EntityId,
                Uuid = this.Uuid,
                Type = this.TypeId,
                Position = this.PairingPosition,
                Pitch = this.Pitch,
                Yaw = this.Yaw,
                Data = additionalData,
                Velocity = velocity ?? new Velocity(0, 0, 0)
            },
            new SetEntityDataPacket
            {
                EntityId = this.EntityId,
                Entity = this
            }
        ];

        // Like vanilla's pairing data, a living entity's syncable attributes follow its data.
        if (this is Living && this.CreateAttributesPacket(changedOnly: false) is UpdateAttributesPacket attributes)
            packets.Add(attributes);

        return new BundledPacket(packets);
    }
}
