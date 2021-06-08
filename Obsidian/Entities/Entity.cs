using Obsidian.API;
using Obsidian.Chat;
using Obsidian.Net;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace Obsidian.Entities
{
    //TODO detect when an entity is swimming
    public class Entity : IEquatable<Entity>, IEntity
    {
        public World World { get; set; }
        public IWorld WorldLocation => World;

        #region Location properties
        internal int TeleportId { get; set; }

        public VectorF LastPosition { get; set; }

        public VectorF Position { get; set; }

        public Angle Pitch { get; set; }

        public Angle Yaw { get; set; }
        #endregion Location properties

        public int EntityId { get; internal set; }

        public Pose Pose { get; set; } = Pose.Standing;

        public int Air { get; set; } = 300;

        public ChatMessage CustomName { get; private set; }

        public bool CustomNameVisible { get; private set; }
        public bool Silent { get; private set; }
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

        public Entity()
        {
        }

        #region Update methods
        internal virtual Task UpdateAsync(Server server, VectorF position, bool onGround)
        {
            var isNewLocation = position != this.Position;

            if (isNewLocation)
            {
                Vector delta = (Vector)(position * 32 - Position * 32) * 128;

                server.BroadcastPacketWithoutQueue(new EntityPosition
                {
                    EntityId = this.EntityId,

                    Delta = delta,

                    OnGround = onGround
                }, this.EntityId);

                this.UpdatePosition(position, onGround);
            }

            return Task.CompletedTask;
        }


        internal virtual Task UpdateAsync(Server server, VectorF position, Angle yaw, Angle pitch, bool onGround)
        {
            var isNewLocation = position != this.Position;
            var isNewRotation = yaw != this.Yaw || pitch != this.Pitch;

            if (isNewLocation)
            {
                Vector delta = (Vector)(position * 32 - Position * 32) * 128;

                if (isNewRotation)
                {
                    server.BroadcastPacketWithoutQueue(new EntityPositionAndRotation
                    {
                        EntityId = this.EntityId,

                        Delta = delta,

                        Yaw = yaw,
                        Pitch = pitch,

                        OnGround = onGround
                    }, this.EntityId);

                    server.BroadcastPacketWithoutQueue(new EntityHeadLook
                    {
                        EntityId = this.EntityId,
                        HeadYaw = yaw
                    });
                }
                else
                {
                    server.BroadcastPacketWithoutQueue(new EntityPosition
                    {
                        EntityId = this.EntityId,

                        Delta = delta,

                        OnGround = onGround
                    }, this.EntityId);
                }

                this.UpdatePosition(position, yaw, pitch, onGround);
            }

            return Task.CompletedTask;
        }


        internal virtual Task UpdateAsync(Server server, Angle yaw, Angle pitch, bool onGround)
        {
            var isNewRotation = yaw != this.Yaw || pitch != this.Pitch;

            if (isNewRotation)
            {
                server.BroadcastPacketWithoutQueue(new EntityRotation
                {
                    EntityId = this.EntityId,
                    OnGround = onGround,
                    Yaw = yaw,
                    Pitch = pitch
                }, this.EntityId);

                server.BroadcastPacketWithoutQueue(new EntityHeadLook
                {
                    EntityId = this.EntityId,
                    HeadYaw = yaw
                });
                this.UpdatePosition(yaw, pitch, onGround);
            }

            return Task.CompletedTask;
        }

        public void UpdatePosition(VectorF pos, bool onGround = true)
        {
            this.Position = pos;
            this.OnGround = onGround;
        }

        public void UpdatePosition(VectorF pos, Angle yaw, Angle pitch, bool onGround = true)
        {
            this.Position = pos;
            this.Yaw = yaw;
            this.Pitch = pitch;
            this.OnGround = onGround;
        }

        public void UpdatePosition(float x, float y, float z, bool onGround = true)
        {
            this.Position = new VectorF(x, y, z);
            this.OnGround = onGround;
        }

        public void UpdatePosition(Angle yaw, Angle pitch, bool onGround = true)
        {
            this.Yaw = yaw;
            this.Pitch = pitch;
            this.OnGround = onGround;
        }
        #endregion

        public VectorF GetLookDirection()
        {
            const float DegreesToRadian = (1 / 255f) * 360f / (180f * MathF.PI);
            float pitch = Pitch.Value * DegreesToRadian;
            float yaw = Yaw.Value * DegreesToRadian;

            float cosPitch = MathF.Cos(pitch);
            return new(-cosPitch * MathF.Sin(yaw), -MathF.Sin(pitch), cosPitch * MathF.Cos(yaw));
        }

        public Task RemoveAsync() => this.World.DestroyEntityAsync(this);

        private EntityBitMask GenerateBitmask()
        {
            var mask = EntityBitMask.None;

            if (this.Sneaking)
            {
                this.Pose = Pose.Sneaking;
                mask |= EntityBitMask.Crouched;
            }
            else if (this.Swimming)
            {
                this.Pose = Pose.Swimming;
                mask |= EntityBitMask.Swimming;
            }
            else if (!this.Sneaking && this.Pose == Pose.Sneaking || !this.Swimming && this.Pose == Pose.Swimming)
                this.Pose = Pose.Standing;
            else if (this.Sprinting)
                mask |= EntityBitMask.Sprinting;
            else if (this.Glowing)
                mask |= EntityBitMask.Glowing;
            else if (this.Invisible)
                mask |= EntityBitMask.Invisible;
            else if (this.Burning)
                mask |= EntityBitMask.OnFire;
            else if (this.FlyingWithElytra)
                mask |= EntityBitMask.FlyingWithElytra;

            return mask;
        }

        public virtual async Task WriteAsync(MinecraftStream stream)
        {
            await stream.WriteEntityMetdata(0, EntityMetadataType.Byte, (byte)this.GenerateBitmask());

            await stream.WriteEntityMetdata(1, EntityMetadataType.VarInt, this.Air);

            await stream.WriteEntityMetdata(2, EntityMetadataType.OptChat, this.CustomName, this.CustomName != null);

            await stream.WriteEntityMetdata(3, EntityMetadataType.Boolean, this.CustomNameVisible);
            await stream.WriteEntityMetdata(4, EntityMetadataType.Boolean, this.Silent);
            await stream.WriteEntityMetdata(5, EntityMetadataType.Boolean, this.NoGravity);
            await stream.WriteEntityMetdata(6, EntityMetadataType.Pose, this.Pose);
        }

        public virtual void Write(MinecraftStream stream)
        {
            stream.WriteEntityMetadataType(0, EntityMetadataType.Byte);

            stream.WriteUnsignedByte((byte)this.GenerateBitmask());

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
        }

        public virtual Task TickAsync() => Task.CompletedTask;

        public bool Equals([AllowNull] Entity other)
        {
            if (other is null)
                return false;

            if (ReferenceEquals(this, other))
                return true;

            return this.EntityId == other.EntityId;
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

        public override int GetHashCode() => this.EntityId.GetHashCode();
    }
}