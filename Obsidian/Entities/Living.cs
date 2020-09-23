using Obsidian.Net;
using Obsidian.Net.Packets.Play.Client;
using Obsidian.Util.DataTypes;
using System;
using System.Threading.Tasks;

namespace Obsidian.Entities
{
    public class Living : Entity
    {
        #region Location properties
        internal Angle LastPitch { get; set; }

        internal Angle LastYaw { get; set; }

        public Angle Pitch { get; set; }

        public Angle Yaw { get; set; }
        #endregion Location properties
        public LivingBitMask LivingBitMask { get; set; }

        public float Health { get; set; }

        public uint ActiveEffectColor { get; private set; }

        public bool OnGround { get; set; }

        public bool AmbientPotionEffect { get; set; }

        public int AbsorbedArrows { get; set; }

        public int AbsorbtionAmount { get; set; }

        public Position BedBlockPosition { get; set; }

        internal async Task UpdateAsync(Server server, Position position, bool onGround)
        {
            var newPos = position * 32 * 64;
            var lastPos = this.LastPosition * 32 * 64;

            short newX = (short)(newPos.X - lastPos.X);
            short newY = (short)(newPos.Y - lastPos.Y);
            short newZ = (short)(newPos.Z - lastPos.Z);

            var isNewLocation = position != this.LastPosition;

            if (isNewLocation)
            {
                await server.BroadcastPacketWithoutQueueAsync(new EntityPosition
                {
                    EntityId = this.EntityId,

                    DeltaX = newX,
                    DeltaY = newY,
                    DeltaZ = newZ,

                    OnGround = onGround
                }, this.EntityId);
                this.UpdatePosition(position, onGround);
            }
        }

        internal async Task UpdateAsync(Server server, Angle yaw, Angle pitch, bool onGround)
        {
            var isNewRotation = yaw.Value != this.LastYaw.Value || pitch.Value != this.LastPitch.Value;

            if (isNewRotation)
            {
                await server.BroadcastPacketWithoutQueueAsync(new EntityRotation
                {
                    EntityId = this.EntityId,
                    OnGround = onGround,
                    Yaw = yaw,
                    Pitch = pitch
                }, this.EntityId);

                this.UpdatePosition(yaw, pitch, onGround);
            }
        }

        internal async Task UpdateAsync(Server server, Position position, Angle yaw, Angle pitch, bool onGround)
        {
            var newPos = position * 32 * 64;
            var lastPos = this.LastPosition * 32 * 64;

            short newX = (short)(newPos.X - lastPos.X);
            short newY = (short)(newPos.Y - lastPos.Y);
            short newZ = (short)(newPos.Z - lastPos.Z);

            var isNewLocation = position != this.LastPosition;

            var isNewRotation = yaw.Value != this.LastYaw.Value || pitch.Value != this.LastPitch.Value;

            if (isNewRotation)
                this.CopyLook();

            if (isNewLocation)
            {
                if (isNewRotation)
                {
                    await server.BroadcastPacketWithoutQueueAsync(new EntityPositionAndRotation
                    {
                        EntityId = this.EntityId,

                        DeltaX = newX,
                        DeltaY = newY,
                        DeltaZ = newZ,

                        Yaw = yaw,

                        Pitch = pitch,

                        OnGround = onGround
                    }, this.EntityId);
                }
                else
                {
                    await server.BroadcastPacketWithoutQueueAsync(new EntityPosition
                    {
                        EntityId = this.EntityId,

                        DeltaX = newX,
                        DeltaY = newY,
                        DeltaZ = newZ,

                        OnGround = onGround
                    }, this.EntityId);
                }

                this.UpdatePosition(position, yaw, pitch, onGround);
            }
        }

        internal void CopyPosition(bool withLook = false)
        {
            this.LastPosition = this.Position;

            if (withLook)
                this.CopyLook();
        }

        internal void CopyLook()
        {
            this.LastYaw = this.Yaw;
            this.LastPitch = this.Pitch;
        }

        public void UpdatePosition(Position pos, bool onGround = true)
        {
            this.CopyPosition();
            this.Position = pos;
            this.OnGround = onGround;
        }

        public void UpdatePosition(Position pos, Angle yaw, Angle pitch, bool onGround = true)
        {
            this.CopyPosition(true);
            this.Position = pos;
            this.Yaw = yaw;
            this.Pitch = pitch;
            this.OnGround = onGround;
        }

        public void UpdatePosition(double x, double y, double z, bool onGround = true)
        {
            this.CopyPosition();
            this.Position.X = x;
            this.Position.Y = y;
            this.Position.Z = z;
            this.OnGround = onGround;
        }

        public void UpdatePosition(Angle yaw, Angle pitch, bool onGround = true)
        {
            this.CopyLook();
            this.Yaw = yaw;
            this.Pitch = pitch;
            this.OnGround = onGround;
        }

        public override async Task WriteAsync(MinecraftStream stream)
        {
            await base.WriteAsync(stream);

            await stream.WriteEntityMetdata(7, EntityMetadataType.Byte, (byte)this.LivingBitMask);

            await stream.WriteEntityMetdata(8, EntityMetadataType.Float, this.Health);

            await stream.WriteEntityMetdata(9, EntityMetadataType.VarInt, (int)this.ActiveEffectColor);

            await stream.WriteEntityMetdata(10, EntityMetadataType.Boolean, this.AmbientPotionEffect);

            await stream.WriteEntityMetdata(11, EntityMetadataType.VarInt, this.AbsorbedArrows);

            await stream.WriteEntityMetdata(12, EntityMetadataType.VarInt, this.AbsorbtionAmount);

            await stream.WriteEntityMetdata(13, EntityMetadataType.OptPosition, this.BedBlockPosition, this.BedBlockPosition != null);
        }

    }

    [Flags]
    public enum LivingBitMask : byte
    {
        None = 0x00,

        HandActive = 0x01,
        ActiveHand = 0x02,
        InRiptideSpinAttack = 0x04
    }
}
