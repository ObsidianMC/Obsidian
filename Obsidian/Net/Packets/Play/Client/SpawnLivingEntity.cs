using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using Obsidian.Util.DataTypes;
using System;

namespace Obsidian.Net.Packets.Play.Client
{
    public class SpawnLivingEntity : Packet
    {
        [Field(0)]
        public int EntityId { get; }

        [Field(1)]
        public Guid Uuid { get; }

        [Field(2)]
        public int Type { get; }

        [Field(3, true)]
        public Position Position { get; }

        [Field(4)]
        public Angle Yaw { get; }

        [Field(5)]
        public Angle Pitch { get; }

        [Field(6)]
        public float HeadPitch { get; }

        [Field(7)]
        public Velocity Velocity { get; }

        [Field(8)]
        public Entity Entity { get; }

        public SpawnLivingEntity(int id, Guid uuid, int type, Position position, Angle yaw, Angle pitch, float headPitch, Velocity velocity, Entity entity) : base(0x03)
        {
            this.EntityId = id;
            this.Uuid = uuid;
            this.Type = type;
            this.Position = Position ?? throw new ArgumentNullException(nameof(position));
            this.Yaw = yaw;
            this.Pitch = pitch;
            this.HeadPitch = headPitch;
            this.Velocity = velocity;
            this.Entity = entity ?? throw new ArgumentNullException(nameof(entity));
        }
    }
}