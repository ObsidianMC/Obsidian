using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using Obsidian.Util.DataTypes;
using System;

namespace Obsidian.Net.Packets.Play
{
    public class SpawnMob : Packet
    {
        [Field(0)]
        public int EntityId { get; }

        [Field(1)]
        public Guid Uuid { get; }

        [Field(2)]
        public int Type { get; }

        [Field(3)]
        public Transform Transform { get; }

        [Field(4)]
        public float HeadPitch { get; }

        [Field(5)]
        public Velocity Velocity { get; }

        [Field(6)]
        public Entity Entity { get; }

        public SpawnMob(int id, Guid uuid, int type, Transform transform, float headPitch, Velocity velocity, Entity entity) : base(0x03)
        {
            this.EntityId = id;
            this.Uuid = uuid;
            this.Type = type;
            this.Transform = transform ?? throw new ArgumentNullException(nameof(transform));
            this.HeadPitch = headPitch;
            this.Velocity = velocity;
            this.Entity = entity ?? throw new ArgumentNullException(nameof(entity));
        }
    }
}