﻿using Obsidian.API.AI;
using Obsidian.Net;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.Entities;

public class Entity : IEquatable<Entity>, IEntity
{
    public IServer Server { get; set; }

    protected Server server => Server as Server;

    public World World { get; set; }
    public IWorld WorldLocation => World;

    #region Location properties
    public VectorF LastPosition { get; set; }

    public VectorF Position { get; set; }

    public Angle Pitch { get; set; }

    public Angle Yaw { get; set; }
    #endregion Location properties

    public int EntityId { get; internal set; }

    public Guid Uuid { get; set; } = Guid.NewGuid();

    public Pose Pose { get; set; } = Pose.Standing;

    public int PowderedSnowTicks { get; set; } = 0;

    public EntityType Type { get; set; }

    public int Air { get; set; } = 300;

    public float Health { get; set; }

    public ChatMessage CustomName { get; set; }

    public bool CustomNameVisible { get; set; }
    public bool Silent { get; set; }
    public bool NoGravity { get; set; }
    public bool OnGround { get; set; }
    public bool Sneaking { get; set; }
    public bool Sprinting { get; set; }
    public bool CanBeSeen { get; set; }
    public bool Glowing { get; set; }
    public bool Invisible { get; set; }
    public bool Burning { get; set; }
    public bool Swimming { get; set; }
    public bool FlyingWithElytra { get; set; }

    public INavigator Navigator { get; set; }
    public IGoalController GoalController { get; set; }

    public Entity()
    {

    }

    #region Update methods
    internal virtual async Task UpdateAsync(VectorF position, bool onGround)
    {
        var isNewLocation = position != Position;

        if (isNewLocation)
        {
            Vector delta = (Vector)(position * 32 - Position * 32) * 128;

            server.BroadcastPacket(new EntityPosition
            {
                EntityId = EntityId,

                Delta = delta,

                OnGround = onGround
            }, EntityId);

            await UpdatePositionAsync(position, onGround);
        }
    }


    internal virtual async Task UpdateAsync(VectorF position, Angle yaw, Angle pitch, bool onGround)
    {
        var isNewLocation = position != Position;
        var isNewRotation = yaw != Yaw || pitch != Pitch;

        if (isNewLocation)
        {
            Vector delta = (Vector)(position * 32 - Position * 32) * 128;

            if (isNewRotation)
            {
                server.BroadcastPacket(new EntityPositionAndRotation
                {
                    EntityId = EntityId,

                    Delta = delta,

                    Yaw = yaw,
                    Pitch = pitch,

                    OnGround = onGround
                }, EntityId);

                server.BroadcastPacket(new EntityHeadLook
                {
                    EntityId = EntityId,
                    HeadYaw = yaw
                });
            }
            else
            {
                server.BroadcastPacket(new EntityPosition
                {
                    EntityId = EntityId,

                    Delta = delta,

                    OnGround = onGround
                }, EntityId);
            }

            await UpdatePositionAsync(position, yaw, pitch, onGround);
        }
    }


    internal virtual Task UpdateAsync(Angle yaw, Angle pitch, bool onGround)
    {
        var isNewRotation = yaw != Yaw || pitch != Pitch;

        if (isNewRotation)
        {
            server.BroadcastPacket(new EntityRotation
            {
                EntityId = EntityId,
                OnGround = onGround,
                Yaw = yaw,
                Pitch = pitch
            }, EntityId);

            server.BroadcastPacket(new EntityHeadLook
            {
                EntityId = EntityId,
                HeadYaw = yaw
            });
            UpdatePosition(yaw, pitch, onGround);
        }

        return Task.CompletedTask;
    }

    public async Task UpdatePositionAsync(VectorF pos, bool onGround = true)
    {
        var chunk = await World.GetChunkAsync((int)MathF.Round(pos.X / 16f), (int)MathF.Round(pos.Z / 16f), false);
        if (chunk != null && chunk.isGenerated)
        {
            Position = pos;
        }

        OnGround = onGround;
    }

    public async Task UpdatePositionAsync(VectorF pos, Angle yaw, Angle pitch, bool onGround = true)
    {
        var chunk = await World.GetChunkAsync((int)MathF.Round(pos.X / 16f), (int)MathF.Round(pos.Z / 16f), false);
        if (chunk != null && chunk.isGenerated)
        {
            Position = pos;
        }

        Yaw = yaw;
        Pitch = pitch;
        OnGround = onGround;
    }

    public async Task UpdatePositionAsync(float x, float y, float z, bool onGround = true)
    {
        await UpdatePositionAsync(new VectorF(x, y, z), onGround);
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

    public Task RemoveAsync() => World.DestroyEntityAsync(this);

    private EntityBitMask GenerateBitmask()
    {
        var mask = EntityBitMask.None;

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

        await stream.WriteEntityMetdata(2, EntityMetadataType.OptChat, CustomName, CustomName != null);

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

        stream.WriteEntityMetadataType(2, EntityMetadataType.OptChat);
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
        stream.WriteVarInt((int)Pose);

        stream.WriteEntityMetadataType(7, EntityMetadataType.VarInt);
        stream.WriteVarInt(PowderedSnowTicks);
    }

    public IEnumerable<IEntity> GetEntitiesNear(float distance) => World.GetEntitiesNear(Position, distance).Where(x => x != this);

    public virtual Task TickAsync() => Task.CompletedTask;


    //TODO check for other entities and handle accordingly 
    public async Task DamageAsync(IEntity source, float amount = 1.0f)
    {
        Health -= amount;

        if (this is ILiving living)
        {
            await server.QueueBroadcastPacketAsync(new EntityAnimationPacket
            {
                EntityId = EntityId,
                Animation = EntityAnimationType.TakeDamage
            });

            if (living is IPlayer iplayer)
            {
                var player = iplayer as Player;

                await player.client.QueuePacketAsync(new UpdateHealth(Health, 20, 5));

                if (!player.Alive)
                    await player.KillAsync(source, ChatMessage.Simple("You died xd"));
            }
        }
    }

    public virtual Task KillAsync(IEntity source) => Task.CompletedTask;
    public virtual Task KillAsync(IEntity source, ChatMessage message) => Task.CompletedTask;

    public bool Equals([AllowNull] Entity other)
    {
        if (other is null)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        return EntityId == other.EntityId;
    }

    public override bool Equals(object obj) => Equals(obj as Entity);

    public static implicit operator int(Entity entity) => entity.EntityId;

    public static bool operator ==(Entity a, Entity b)
    {
        if (ReferenceEquals(a, b))
            return true;

        return a.Equals(b);
    }

    public static bool operator !=(Entity a, Entity b) => !(a == b);

    public override int GetHashCode() => EntityId.GetHashCode();

    public virtual Task TeleportAsync(IWorld world) => Task.CompletedTask;

    public async virtual Task TeleportAsync(IEntity to)
    {
        Position = to.Position;

        if (to.WorldLocation != World)
        {
            await World.DestroyEntityAsync(this);

            World = to.WorldLocation as World;

            await World.SpawnEntityAsync(to.Position, Type);

            return;
        }

        if (VectorF.Distance(Position, to.Position) > 8)
        {
            await server.QueueBroadcastPacketAsync(new EntityTeleport
            {
                EntityId = EntityId,
                OnGround = OnGround,
                Position = to.Position,
                Pitch = Pitch,
                Yaw = Yaw,
            });

            return;
        }

        var delta = (Vector)(to.Position * 32 - Position * 32) * 128;

        await server.QueueBroadcastPacketAsync(new EntityPositionAndRotation
        {
            EntityId = EntityId,
            Delta = delta,
            OnGround = OnGround,
            Pitch = Pitch,
            Yaw = Yaw
        });
    }

    public async virtual Task TeleportAsync(VectorF pos)
    {
        if (VectorF.Distance(Position, pos) > 8)
        {
            await server.QueueBroadcastPacketAsync(new EntityTeleport
            {
                EntityId = EntityId,
                OnGround = OnGround,
                Position = pos,
                Pitch = Pitch,
                Yaw = Yaw,
            });

            return;
        }

        var delta = (Vector)(pos * 32 - Position * 32) * 128;

        await server.QueueBroadcastPacketAsync(new EntityPositionAndRotation
        {
            EntityId = EntityId,
            Delta = delta,
            OnGround = OnGround,
            Pitch = Pitch,
            Yaw = Yaw
        });
    }
}
