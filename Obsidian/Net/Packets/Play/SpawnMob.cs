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
        public SpawnMob(int id, Guid uuid, int type, Transform transform, byte headPitch, Velocity velocity, Entity entity) : base(0x03)
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
        public byte HeadPitch { get; }
        public Velocity Velocity { get; }
        public Entity Entity { get; }

        public override Task PopulateAsync() => throw new NotImplementedException();

        public override async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MinecraftStream())
            {
                await stream.WriteVarIntAsync(Id);
                await stream.WriteUuidAsync(Uuid);
                await stream.WritePositionAsync(Transform.Position);
                await stream.WriteUnsignedByteAsync(HeadPitch);
                await stream.WriteShortAsync(Velocity.X);
                await stream.WriteShortAsync(Velocity.Y);
                await stream.WriteShortAsync(Velocity.Z);
                await Entity.WriteAsync(stream);

                return stream.ToArray();
            }
        }
    }
}