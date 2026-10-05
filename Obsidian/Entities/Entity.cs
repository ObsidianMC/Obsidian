using Obsidian.API.AI;
using Obsidian.API.World;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.Entities;

public class Entity : IEquatable<Entity>, IEntity
{
    protected virtual ConcurrentDictionary<string, float> Attributes { get; } = new();

    protected byte MetadataIndex { get; set; }

    public required ILevel Level { get; set; }

    public IPacketBroadcaster PacketBroadcaster => this.Level.PacketBroadcaster;
    public IEventDispatcher EventDispatcher => this.Level.EventDispatcher;

    #region Location properties
    public VectorD LastPosition { get; set; }

    public VectorD Position { get; set; }

    public Angle Pitch { get; set; }

    public Angle Yaw { get; set; }

    /// <summary>
    /// The entity's velocity in blocks per tick, saved as vanilla's <c>Motion</c>.
    /// </summary>
    public VectorD Motion { get; set; }
    #endregion Location properties

    public int EntityId { get; internal set; }

    public Guid Uuid { get; set; } = Guid.NewGuid();

    public Pose Pose { get; set; } = Pose.Standing;

    public virtual BoundingBox BoundingBox { get; protected set; } = new(VectorD.Zero, VectorD.Zero);
    public virtual EntityDimension Dimension { get; protected set; } = EntityDimension.Zero;
    protected virtual float DimensionScale => 1;

    public int PowderedSnowTicks { get; set; }

    public EntityType Type { get; set; }

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
        tag.Set(EntityNbt.DoubleList("Pos", this.Position));
        tag.Set(EntityNbt.DoubleList("Motion", this.Motion));
        tag.Set(new NbtList(NbtTagType.Float, "Rotation")
        {
            new NbtTag<float>(string.Empty, this.Yaw.Degrees),
            new NbtTag<float>(string.Empty, this.Pitch.Degrees)
        });
        tag.Set(new NbtTag<short>("Air", this.Air));
        tag.Set(new NbtTag<bool>("OnGround", this.MovementFlags.HasFlag(MovementFlags.OnGround)));
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
        if (tag.TryGetTag<NbtArray<int>>("UUID", out var uuid) && uuid.Count == 4)
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
        if (chunk != null && chunk.IsGenerated)
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

    public async virtual ValueTask RemoveAsync() => await this.Level.DestroyEntityAsync(this);

    protected virtual EntityBitMask GenerateBitmask()
    {
        EntityBitMask mask = EntityBitMask.None;

        if (Sneaking)
        {
            Pose = Pose.Sneaking;
            mask |= EntityBitMask.Crouched;
        }
        else if (Swimming)
        {
            Pose = Pose.Swimming;
            mask |= EntityBitMask.Swimming;
        }
        else if (!Sneaking && Pose == Pose.Sneaking || !Swimming && Pose == Pose.Swimming)
            Pose = Pose.Standing;
        else if (Sprinting)
            mask |= EntityBitMask.Sprinting;
        else if (Glowing)
            mask |= EntityBitMask.Glowing;
        else if (Invisible)
            mask |= EntityBitMask.Invisible;
        else if (Burning)
            mask |= EntityBitMask.OnFire;
        else if (FlyingWithElytra)
            mask |= EntityBitMask.FlyingWithElytra;

        return mask;
    }

    // TODO: Source generate the metadata types and their indexes for each type to avoid this and potential bugs with index ordering
    public virtual void Write(INetStreamWriter writer)
    {
        //Reset index for writing metadata, so that it starts from 0 for each entity, might be better to statically assign these indexes for each type, but this works for now
        this.MetadataIndex = 0;

        this.WriteEntityMetadataType(writer, EntityMetadataType.Byte);
        writer.WriteByte(GenerateBitmask());

        this.WriteEntityMetadataType(writer, EntityMetadataType.VarInt);
        writer.WriteVarInt(Air);

        this.WriteEntityMetadataType(writer, EntityMetadataType.OptionalTextComponent);
        writer.WriteOptional(CustomName);

        this.WriteEntityMetadataType(writer, EntityMetadataType.Boolean);
        writer.WriteBoolean(CustomNameVisible);

        this.WriteEntityMetadataType(writer, EntityMetadataType.Boolean);
        writer.WriteBoolean(Silent);

        this.WriteEntityMetadataType(writer, EntityMetadataType.Boolean);
        writer.WriteBoolean(NoGravity);

        this.WriteEntityMetadataType(writer, EntityMetadataType.Pose);
        writer.WriteVarInt(this.Pose);

        this.WriteEntityMetadataType(writer, EntityMetadataType.VarInt);
        writer.WriteVarInt(PowderedSnowTicks);
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
        if (this is IPlayer attackedPlayer && !ReferenceEquals(source, this))
            Wolf.AlertOwnedWolves(attackedPlayer, source);

        if (this is ILiving living)
        {
            this.PacketBroadcaster.QueuePacketToLevel(this.Level, new HurtAnimationPacket
            {
                EntityId = EntityId,
                Yaw = (float)(Math.Atan2(source.Position.Z - Position.Z, source.Position.X - Position.X) * 180 / Math.PI - Yaw.Degrees)
            });

            if (living is Player player)
            {
                player.AddExhaustion(0.1f);
                await player.Client.QueuePacketAsync(new SetHealthPacket(Math.Max(0, Health), player.FoodLevel, player.FoodSaturationLevel));

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

    public static bool operator ==(Entity a, Entity b)
    {
        if (ReferenceEquals(a, b))
            return true;

        return a.Equals(b);
    }

    public static bool operator !=(Entity a, Entity b) => !(a == b);

    public override int GetHashCode() => EntityId.GetHashCode();

    public virtual ValueTask TeleportAsync(IWorld world) => default;

    public async virtual ValueTask TeleportAsync(IEntity to)
    {
        if (to is not Entity target)
            return;

        if (to.Level != Level)
        {
            await Level.DestroyEntityAsync(this);

            Level = target.Level;
            Level.SpawnEntity(to.Position, Type);

            return;
        }

        await this.TeleportAsync(to.Position);
    }

    public virtual ValueTask TeleportAsync(VectorD pos)
    {
        var delta = MoveDelta(Position, pos);
        if (!IsRelativeMove(delta))
        {
            this.PacketBroadcaster.QueuePacketToLevel(this.Level, 0, new TeleportEntityPacket
            {
                EntityId = EntityId,
                OnGround = MovementFlags.HasFlag(MovementFlags.OnGround),
                Position = pos,
                Pitch = Pitch,
                Yaw = Yaw
            });

            return default;
        }

        this.PacketBroadcaster.QueuePacketToLevel(this.Level, 0, new MoveEntityPosRotPacket
        {
            EntityId = EntityId,
            Delta = delta,
            OnGround = MovementFlags.HasFlag(MovementFlags.OnGround),
            Pitch = Pitch,
            Yaw = Yaw
        });

        return default;
    }

    public virtual void SpawnEntity(Velocity? velocity = null, int additionalData = 0)
    {
        var packet = CreateSpawnPacket(velocity, additionalData);
        var range = Level is Obsidian.WorldData.AbstractLevel level ? level.Configuration.EntityBroadcastRangePercentage : 100;
        foreach (var player in Level.GetPlayersInRange(Position, range).OfType<Player>())
        {
            if (player.EntityId == EntityId)
                continue;
            var (x, z) = Position.ToChunkCoord();
            if (this is not Player && !player.LoadedChunks.Contains(NumericsHelper.IntsToLong(x, z)))
                continue;
            PacketBroadcaster.QueuePacketTo(packet, ids: [player.EntityId]);
            if (this is not Player)
                player.TrackedEntities[EntityId] = Uuid;
        }
    }

    internal virtual IClientboundPacket CreateSpawnPacket(Velocity? velocity = null, int additionalData = 0) => new BundledPacket
        (
             [
                new AddEntityPacket
                {
                    EntityId = this.EntityId,
                    Uuid = this.Uuid,
                    Type = this.Type,
                    Position = this.Position,
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
            ]
        );

    public bool TryAddAttribute(string attributeResourceName, float value) =>
        Attributes.TryAdd(ResolveAttributeName(attributeResourceName), value);

    public bool TryUpdateAttribute(string attributeResourceName, float newValue)
    {
        attributeResourceName = ResolveAttributeName(attributeResourceName);
        if (!Attributes.TryGetValue(attributeResourceName, out var value))
            return false;

        return Attributes.TryUpdate(attributeResourceName, newValue, value);
    }

    public bool HasAttribute(string attributeResourceName) =>
        Attributes.ContainsKey(ResolveAttributeName(attributeResourceName));

    public float GetAttributeValue(string attributeResourceName) =>
        Attributes.GetValueOrDefault(ResolveAttributeName(attributeResourceName));

    private string ResolveAttributeName(string name)
    {
        // Keep saved legacy overrides, while allowing AI to use the regenerated vanilla attribute names.
        if (Attributes.ContainsKey(name))
            return name;

        const string genericPrefix = "minecraft:generic.";
        if (name.StartsWith(genericPrefix, StringComparison.Ordinal))
            return "minecraft:" + name[genericPrefix.Length..];

        return name == "minecraft:horse.jump_strength" ? "minecraft:jump_strength" : name;
    }
}
