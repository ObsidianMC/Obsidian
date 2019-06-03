using Obsidian.Util;
using System.Threading.Tasks;

namespace Obsidian.Packets
{
    public class PlayerPosition : Packet
    {
        public PlayerPosition(double x, double y, double z, bool onground) : base(0x10, new byte[0])
        {
            this.X = x;
            this.Y = y;
            this.Z = z;
            this.OnGround = onground;
        }

        public PlayerPosition(byte[] data) : base(0x10, data) { }

        public double X { get; private set; } = 0;

        public double Y { get; private set; } = 0;

        public double Z { get; private set; } = 0;

        public bool OnGround { get; private set; } = false;

        protected override async Task PopulateAsync()
        {
            using (var stream = new MinecraftStream(this._packetData))
            {
                this.X = await stream.ReadDoubleAsync();
                this.Y = await stream.ReadDoubleAsync();
                this.Z = await stream.ReadDoubleAsync();
                this.OnGround = await stream.ReadBooleanAsync();
            }
        }

        public override async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MinecraftStream())
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