using Obsidian.API.AI;
using Obsidian.Net.Packets.Play.Clientbound;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.Entities;

public class Entity : IEquatable<Entity>, IEntity
{
    protected virtual ConcurrentDictionary<string, float> Attributes { get; } = new();

    public required IWorld World { get; set; }

    public IPacketBroadcaster PacketBroadcaster => this.World.PacketBroadcaster;
    public IEventDispatcher EventDispatcher => this.World.EventDispatcher;

    #region Location properties
    public VectorF LastPosition { get; set; }

    public VectorF Position { get; set; }

    public Angle Pitch { get; set; }

    public Angle Yaw { get; set; }
    #endregion Location properties

    public int EntityId { get; internal set; }

    public Guid Uuid { get; set; } = Guid.NewGuid();

    public Pose Pose { get; set; } = Pose.Standing;

    public virtual BoundingBox BoundingBox { get; protected set; } = new(VectorF.Zero, VectorF.Zero);
    public virtual EntityDimension Dimension { get; protected set; } = EntityDimension.Zero;

    public int PowderedSnowTicks { get; set; } = 0;

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

    #region Update methods
    public virtual async ValueTask UpdateAsync(VectorF position, MovementFlags movementFlags)
    {
        var isNewLocation = position != Position;

        if (isNewLocation)
        {
            var delta = (Vector)((position * 32 - Position * 32) * 128);

            this.PacketBroadcaster.BroadcastToWorldInRange(this.World, position, new MoveEntityPosPacket
            {
                EntityId = EntityId,

                Delta = delta,

                OnGround = movementFlags.HasFlag(MovementFlags.OnGround)
            }, EntityId);
        }

        await UpdatePositionAsync(position, movementFlags);
    }

    public virtual async ValueTask UpdateAsync(VectorF position, Angle yaw, Angle pitch, MovementFlags movementFlags)
    {
        var isNewLocation = position != Position;
        var isNewRotation = yaw != Yaw || pitch != Pitch;

        if (isNewLocation)
        {
            var delta = (Vector)((position * 32 - Position * 32) * 128);

            if (isNewRotation)
            {
                this.PacketBroadcaster.BroadcastToWorldInRange(this.World, position, new MoveEntityPosRotPacket
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
                this.PacketBroadcaster.BroadcastToWorldInRange(this.World, position, new MoveEntityPosPacket
                {
                    EntityId = EntityId,

                    Delta = delta,

                    OnGround = movementFlags.HasFlag(MovementFlags.OnGround)
                }, EntityId);
            }
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
        if (this.World != entity.World)
            return false;

        var locationDifference = LocationDiff.GetDifference(this.Position, entity.Position);

        distance *= distance;

        return locationDifference.CalculatedDifference <= distance;
    }


    public void SetHeadRotation(Angle headYaw) =>
        this.PacketBroadcaster.BroadcastToWorldInRange(this.World, this.Position, new RotateHeadPacket
        {
            EntityId = EntityId,
            HeadYaw = headYaw
        }, EntityId);

    public void SetRotation(Angle yaw, Angle pitch, MovementFlags movementFlags)
    {
        this.PacketBroadcaster.BroadcastToWorldInRange(this.World, this.Position, new MoveEntityRotPacket
        {
            EntityId = EntityId,
            OnGround = movementFlags.HasFlag(MovementFlags.OnGround),
            Yaw = yaw,
            Pitch = pitch
        }, EntityId);

        this.UpdatePosition(yaw, pitch, movementFlags);
    }

    public async Task UpdatePositionAsync(VectorF pos, MovementFlags movementFlags)
    {
        var (x, z) = pos.ToChunkCoord();
        var chunk = await this.World.GetChunkAsync(x, z, false);
        if (chunk != null && chunk.IsGenerated)
        {
            Position = pos;
        }

        MovementFlags = movementFlags;

        if (Dimension != EntityDimension.Zero)
            BoundingBox = Dimension.CreateBBFromPosition(pos);
    }

    public async Task UpdatePositionAsync(VectorF pos, Angle yaw, Angle pitch, MovementFlags movementFlags = MovementFlags.OnGround)
    {
        var (x, z) = pos.ToChunkCoord();
        var chunk = await World.GetChunkAsync(x, z, false);
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
        const float DegreesToRadian = (1 / 255f) * 360f / (180f * MathF.PI);
        float pitch = Pitch.Value * DegreesToRadian;
        float yaw = Yaw.Value * DegreesToRadian;

        (float sinPitch, float cosPitch) = MathF.SinCos(pitch);
        (float sinYaw, float cosYaw) = MathF.SinCos(yaw);
        return new(-cosPitch * sinYaw, -sinPitch, cosPitch * cosYaw);
    }

    public async virtual ValueTask RemoveAsync() => await this.World.DestroyEntityAsync(this);

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
    }

    public IEnumerable<IEntity> GetEntitiesNear(float distance) => World.GetEntitiesInRange(Position, distance).Where(x => x != this);

    //TODO GRAVITY
    public virtual ValueTask TickAsync() => default;

    //TODO check for other entities and handle accordingly 
    public async ValueTask DamageAsync(IEntity source, float amount = 1.0f)
    {
        Health -= amount;

        if (this is ILiving living)
        {
            this.PacketBroadcaster.QueuePacketToWorld(this.World, new AnimatePacket
            {
                EntityId = EntityId,
                Animation = EntityAnimationType.CriticalEffect
            });

            if (living is Player player)
            {
                await player.Client.QueuePacketAsync(new SetHealthPacket(Health, 20, 5));

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

        if (to.World != World)
        {
            await World.DestroyEntityAsync(this);

            World = target.World;
            World.SpawnEntity(to.Position, Type);

            return;
        }

        await this.TeleportAsync(to.Position);
    }

    public virtual ValueTask TeleportAsync(VectorF pos)
    {
        if (VectorF.Distance(Position, pos) > 8)
        {
            this.PacketBroadcaster.QueuePacketToWorld(this.World, 0, new TeleportEntityPacket
            {
                EntityId = EntityId,
                OnGround = MovementFlags.HasFlag(MovementFlags.OnGround),
                Position = pos,
                Pitch = Pitch,
                Yaw = Yaw
            });

            return default;
        }

        var delta = (Vector)(pos * 32 - Position * 32) * 128;

        this.PacketBroadcaster.QueuePacketToWorld(this.World, 0, new MoveEntityPosRotPacket
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
        this.PacketBroadcaster.QueuePacketToWorldInRange(this.World, this.Position, new BundledPacket
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
        ), this.EntityId);
    }

    public bool TryAddAttribute(string attributeResourceName, float value) =>
        Attributes.TryAdd(attributeResourceName, value);

    public bool TryUpdateAttribute(string attributeResourceName, float newValue)
    {
        if (!Attributes.TryGetValue(attributeResourceName, out var value))
            return false;

        return Attributes.TryUpdate(attributeResourceName, newValue, value);
    }

    public bool HasAttribute(string attributeResourceName) =>
        Attributes.ContainsKey(attributeResourceName);

    public float GetAttributeValue(string attributeResourceName) =>
        Attributes.GetValueOrDefault(attributeResourceName);
}
