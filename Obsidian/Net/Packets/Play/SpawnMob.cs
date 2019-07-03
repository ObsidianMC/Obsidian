using Obsidian.Entities;
using Obsidian.Util;
using System;

namespace Obsidian.Net.Packets.Play
{
    public class SpawnMob : Packet
    {
        public SpawnMob(int id, Guid uuid, int type, Transform transform, float headPitch, Velocity velocity, Entity entity) : base(0x03)
        {
            this.Id = id;
            this.Uuid = uuid;
            this.Type = type;
            this.Transform = transform ?? throw new ArgumentNullException(nameof(transform));
            this.HeadPitch = headPitch;
            this.Velocity = velocity;
            this.Entity = entity ?? throw new ArgumentNullException(nameof(entity));
        }

        [Variable]
        public int Id { get; }

        [Variable]
        public Guid Uuid { get; }

        [Variable]
        public int Type { get; }

        [Variable]
        public Transform Transform { get; }

        [Variable]
        public float HeadPitch { get; }

        [Variable]
        public Velocity Velocity { get; }

        [Variable]
        public Entity Entity { get; }
    }
}