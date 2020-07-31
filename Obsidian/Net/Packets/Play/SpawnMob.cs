using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using Obsidian.Util.DataTypes;
using System;

namespace Obsidian.Net.Packets.Play
{
    public class SpawnMob : Packet
    {
        [PacketOrder(0)]
        public int EntityId { get; }

        [PacketOrder(1)]
        public Guid Uuid { get; }

        [PacketOrder(2)]
        public int Type { get; }

        [PacketOrder(3)]
        public Transform Transform { get; }

        [PacketOrder(4)]
        public float HeadPitch { get; }

        [PacketOrder(5)]
        public Velocity Velocity { get; }

        [PacketOrder(6)]
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