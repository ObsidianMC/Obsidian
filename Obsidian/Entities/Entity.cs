using Obsidian.API.AI;
using Obsidian.Net;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Services;
using Obsidian.WorldData;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.Entities;

public class Entity : IEquatable<Entity>, IEntity
{
    protected virtual ConcurrentDictionary<string, float> Attributes { get; } = new();

    public required IPacketBroadcaster PacketBroadcaster { get; init; }

    public required IWorld World { get => world; init => world = (World)value; }
    internal World world = null!;

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

    public int Air { get; set; } = 300;

    public float Health { get; set; }

    public ChatMessage? CustomName { get; set; }

    public virtual string? TranslationKey { get; protected set; }

    public bool CustomNameVisible { get; set; }
    public bool Silent { get; set; }
    public bool NoGravity { get; set; }
    public bool OnGround { get; set; }
    public bool Sneaking { get; set; }
    public bool Sprinting { get; set; }
    public bool CanBeSeen { get; set; }//What does this do???
    public bool Glowing { get; set; }
    public bool Invisible { get; set; }
    public bool Burning { get; set; }
    public bool Swimming { get; set; }
    public bool FlyingWithElytra { get; set; }

    public virtual bool Summonable { get; set; }

    public virtual bool IsFireImmune { get; set; }

    public INavigator? Navigator { get; set; }
    public IGoalController? GoalController { get; set; }

    #region Update methods
    internal virtual async ValueTask UpdateAsync(VectorF position, bool onGround)
    {
        var isNewLocation = position != Position;

        if (isNewLocation)
        {
            var delta = (Vector)((position * 32 - Position * 32) * 128);

            this.PacketBroadcaster.BroadcastToWorldInRange(this.World, position, new UpdateEntityPositionPacket
            {
                EntityId = EntityId,

                Delta = delta,

                OnGround = onGround
            }, EntityId);
        }

        await UpdatePositionAsync(position, onGround);
    }

    internal virtual async ValueTask UpdateAsync(VectorF position, Angle yaw, Angle pitch, bool onGround)
    {
        var isNewLocation = position != Position;
        var isNewRotation = yaw != Yaw || pitch != Pitch;

        if (isNewLocation)
        {
            var delta = (Vector)((position * 32 - Position * 32) * 128);

            if (isNewRotation)
            {
                this.PacketBroadcaster.BroadcastToWorldInRange(this.World, position, new UpdateEntityPositionAndRotationPacket
                {
                    EntityId = EntityId,

                    Delta = delta,

                    Yaw = yaw,
                    Pitch = pitch,

                    OnGround = onGround
                }, EntityId);

                this.SetHeadRotation(yaw);
            }
            else
            {
                this.PacketBroadcaster.BroadcastToWorldInRange(this.World, position, new UpdateEntityPositionPacket
                {
                    EntityId = EntityId,

                    Delta = delta,

                    OnGround = onGround
                }, EntityId);
            }
        }

        await UpdatePositionAsync(position, yaw, pitch, onGround);
    }

    internal virtual ValueTask UpdateAsync(Angle yaw, Angle pitch, bool onGround)
    {
        var isNewRotation = yaw != Yaw || pitch != Pitch;

        if (isNewRotation)
        {
            this.SetRotation(yaw, pitch, onGround);
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
        this.PacketBroadcaster.BroadcastToWorldInRange(this.World, this.Position, new SetHeadRotationPacket
        {
            EntityId = EntityId,
            HeadYaw = headYaw
        }, EntityId);

    public void SetRotation(Angle yaw, Angle pitch, bool onGround = true)
    {
        this.PacketBroadcaster.BroadcastToWorldInRange(this.World, this.Position, new UpdateEntityRotationPacket
        {
            EntityId = EntityId,
            OnGround = onGround,
            Yaw = yaw,
            Pitch = pitch
        }, EntityId);

        this.UpdatePosition(yaw, pitch, onGround);
    }

    public async Task UpdatePositionAsync(VectorF pos, bool onGround = true)
    {
        var (x, z) = pos.ToChunkCoord();
        var chunk = await world.GetChunkAsync(x, z, false);
        if (chunk != null && chunk.IsGenerated)
        {
            Position = pos;
        }

        OnGround = onGround;

        if (Dimension != EntityDimension.Zero)
            BoundingBox = Dimension.CreateBBFromPosition(pos);
    }

    public async Task UpdatePositionAsync(VectorF pos, Angle yaw, Angle pitch, bool onGround = true)
    {
        var (x, z) = pos.ToChunkCoord();
        var chunk = await world.GetChunkAsync(x, z, false);
        if (chunk is { IsGenerated: true })
        {
            Position = pos;
        }

        Yaw = yaw;
        Pitch = pitch;
        OnGround = onGround;

        if (Dimension != EntityDimension.Zero)
            BoundingBox = Dimension.CreateBBFromPosition(pos);
    }

    public void UpdatePosition(Angle yaw, Angle pitch, bool onGround = true)
    {
        Yaw = yaw;
        Pitch = pitch;
        OnGround = onGround;
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

    public virtual async ValueTask RemoveAsync() => await this.world.DestroyEntityAsync(this);

    protected EntityBitMask GenerateBitmask()
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

    public virtual async Task WriteAsync(MinecraftStream stream)
    {
        await stream.WriteEntityMetdata(0, EntityMetadataType.Byte, (byte)GenerateBitmask());

        await stream.WriteEntityMetdata(1, EntityMetadataType.VarInt, Air);

        await stream.WriteEntityMetdata(2, EntityMetadataType.OptionalTextComponent, CustomName!, CustomName != null);

        await stream.WriteEntityMetdata(3, EntityMetadataType.Boolean, CustomNameVisible);
        await stream.WriteEntityMetdata(4, EntityMetadataType.Boolean, Silent);
        await stream.WriteEntityMetdata(5, EntityMetadataType.Boolean, NoGravity);
        await stream.WriteEntityMetdata(6, EntityMetadataType.Pose, Pose);
        await stream.WriteEntityMetdata(7, EntityMetadataType.VarInt, PowderedSnowTicks);
    }

    public virtual void Write(MinecraftStream stream)
    {
        stream.WriteEntityMetadataType(0, EntityMetadataType.Byte);

        stream.WriteUnsignedByte((byte)GenerateBitmask());

        stream.WriteEntityMetadataType(1, EntityMetadataType.VarInt);
        stream.WriteVarInt(Air);

        stream.WriteEntityMetadataType(2, EntityMetadataType.OptionalTextComponent);
        stream.WriteBoolean(CustomName is not null);
        if (CustomName is not null)
            stream.WriteChat(CustomName);

        stream.WriteEntityMetadataType(3, EntityMetadataType.Boolean);
        stream.WriteBoolean(CustomNameVisible);

        stream.WriteEntityMetadataType(4, EntityMetadataType.Boolean);
        stream.WriteBoolean(Silent);

        stream.WriteEntityMetadataType(5, EntityMetadataType.Boolean);
        stream.WriteBoolean(NoGravity);

        stream.WriteEntityMetadataType(6, EntityMetadataType.Pose);
        stream.WriteVarInt((int)this.Pose);

        stream.WriteEntityMetadataType(7, EntityMetadataType.VarInt);
        stream.WriteVarInt(PowderedSnowTicks);
    }

    public IEnumerable<IEntity> GetEntitiesNear(float distance) => world.GetEntitiesInRange(Position, distance).Where(x => x != this);

    //TODO GRAVITY
    public virtual ValueTask TickAsync() => default;

    //TODO check for other entities and handle accordingly 
    public async ValueTask DamageAsync(IEntity source, float amount = 1.0f)
    {
        Health -= amount;

        if (this is ILiving living)
        {
            this.PacketBroadcaster.QueuePacketToWorld(this.World, new EntityAnimationPacket
            {
                EntityId = EntityId,
                Animation = EntityAnimationType.CriticalEffect
            });

            if (living is Player player)
            {
                await player.client.QueuePacketAsync(new SetHealthPacket(Health, 20, 5));

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

        if (to.World != world)
        {
            await world.DestroyEntityAsync(this);

            world = target.world;
            world.SpawnEntity(to.Position, Type);

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
                OnGround = OnGround,
                Position = pos,
                Pitch = Pitch,
                Yaw = Yaw
            });

            return default;
        }

        var delta = (Vector)(pos * 32 - Position * 32) * 128;

        this.PacketBroadcaster.QueuePacketToWorld(this.World, 0, new UpdateEntityPositionAndRotationPacket
        {
            EntityId = EntityId,
            Delta = delta,
            OnGround = OnGround,
            Pitch = Pitch,
            Yaw = Yaw
        });

        return default;
    }

    public virtual void SpawnEntity(Velocity? velocity = null, int additionalData = 0)
    {
        this.PacketBroadcaster.QueuePacketToWorldInRange(this.World, this.Position, new BundledPacket
        {
            Packets = [
                        new SpawnEntityPacket
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
                        new SetEntityMetadataPacket
                        {
                            EntityId = this.EntityId,
                            Entity = this
                        }
                    ]
        }, this.EntityId);
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
