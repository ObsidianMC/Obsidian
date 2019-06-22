using Obsidian.Entities;
using Obsidian.Util;
using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play
{
    public class SpawnPlayer : Packet
    {
        public int Id { get; set; }

        public Guid Uuid { get; set; }

        public string Uuid3 { get; set; }

        public Transform Tranform { get; set; }

        public Player Player { get; set; }

        public SpawnPlayer() : base (0x05, new byte[0]) { }

        public override Task PopulateAsync()
        {
            throw new NotImplementedException();
        }

        public override async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MinecraftStream())
            {
                await stream.WriteVarIntAsync(this.Id);
                if (string.IsNullOrEmpty(Uuid3))
                {
                    await stream.WriteUuidAsync(this.Uuid);

                }
                else
                {
                    await stream.WriteStringAsync(this.Uuid3);
                }

                await stream.WriteDoubleAsync(this.Tranform.X);
                await stream.WriteDoubleAsync(this.Tranform.Y);
                await stream.WriteDoubleAsync(this.Tranform.Z);

                await stream.WriteUnsignedByteAsync((byte)this.Tranform.Yaw);
                await stream.WriteDoubleAsync((byte)this.Tranform.Pitch);

                await Player.WriteAsync(stream);

                return stream.ToArray();
            }
        }
    }
}
