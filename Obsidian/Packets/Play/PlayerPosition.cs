using Obsidian.Entities;
using Obsidian.Packets.Handshaking;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Obsidian.Packets
{
    public class PlayerPosition
    {
        public PlayerPosition(double x, double y, double z, bool onground)
        {
            this.X = x;
            this.Y = y;
            this.Z = z;
        }

        public double X { get; private set; } = 0;

        public double Y { get; private set; } = 0;

        public double Z { get; private set; } = 0;

        public bool OnGround { get; private set; } = false;

        public static async Task<PlayerPosition> FromArrayAsync(byte[] data)
        {
            using (MemoryStream stream = new MemoryStream(data))
            {
                return new PlayerPosition(await stream.ReadDoubleAsync(), await stream.ReadDoubleAsync(),
                    await stream.ReadDoubleAsync(), await stream.ReadBooleanAsync());
            }
        }

        public async Task<byte[]> ToArrayAsync()
        {
            using (MemoryStream stream = new MemoryStream())
            {
                await stream.WriteDoubleAsync(this.X);
                await stream.WriteDoubleAsync(this.Y);
                await stream.WriteDoubleAsync(this.Z);
                await stream.WriteBooleanAsync(this.OnGround);
                return stream.ToArray();
            }
        }
    }
}