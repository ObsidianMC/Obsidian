using Obsidian.Entities;
using Obsidian.Util;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

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

        public int Id { get; }
        public Guid Uuid { get; }
        public int Type { get; }
        public Transform Transform { get; }
        public float HeadPitch { get; }
        public Velocity Velocity { get; }
        public Entity Entity { get; }

        protected override Task PopulateAsync(MinecraftStream stream) => throw new NotImplementedException();

        protected override async Task ComposeAsync(MinecraftStream stream)
        {
            await stream.WriteVarIntAsync(Id);

            await stream.WriteUuidAsync(Uuid);

            await stream.WriteVarIntAsync(this.Type);

            await stream.WriteDoubleAsync(Transform.X);
            await stream.WriteDoubleAsync(Transform.Y);
            await stream.WriteDoubleAsync(Transform.Z);

            await stream.WriteByteAsync(0);
            await stream.WriteByteAsync(0);
            await stream.WriteByteAsync(0);

            await stream.WriteShortAsync(Velocity.X);
            await stream.WriteShortAsync(Velocity.Y);
            await stream.WriteShortAsync(Velocity.Z);
            //await Entity.WriteAsync(stream);
        }
    }
}