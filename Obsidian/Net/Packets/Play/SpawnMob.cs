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

        public SpawnMob(int id, string uuid3, int type, Transform transform, byte headPitch, Velocity velocity, Entity entity) : base(0x03)
        {
            this.Id = id;
            this.Uuid3 = uuid3;
            this.Type = type;
            this.Transform = transform ?? throw new ArgumentNullException(nameof(transform));
            this.HeadPitch = headPitch;
            this.Velocity = velocity;
            this.Entity = entity ?? throw new ArgumentNullException(nameof(entity));
        }

        public int Id { get; }
        public Guid Uuid { get; }
        public string Uuid3 { get; }
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
                if (Uuid3 != null)
                {
                    await stream.WriteStringAsync(Uuid3);
                }
                else
                {
                    await stream.WriteUuidAsync(Uuid);
                }
                await stream.WriteVarIntAsync(this.Id);

                //await stream.WritePositionAsync();
                await stream.WriteDoubleAsync(Transform.Position.X);
                await stream.WriteDoubleAsync(Transform.Position.Y);
                await stream.WriteDoubleAsync(Transform.Position.Z);

                await stream.WriteUnsignedByteAsync((byte)Transform.Yaw);
                await stream.WriteUnsignedByteAsync((byte)Transform.Pitch);

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