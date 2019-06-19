using Obsidian.Entities;
using Obsidian.Util;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    public class PlayerPosition : Packet
    {
        public PlayerPosition(Position pos, bool onground) : base(0x10, new byte[0])
        {
            this.Position = pos;
            this.OnGround = onground;
        }

        public PlayerPosition(byte[] data) : base(0x10, data) { }

        public Position Position { get; set; }

        public bool OnGround { get; private set; } = false;

        public override async Task PopulateAsync()
        {
            using (var stream = new MinecraftStream(this.PacketData))
            {
                this.Position.X = await stream.ReadDoubleAsync();
                this.Position.Y = await stream.ReadDoubleAsync();
                this.Position.Z = await stream.ReadDoubleAsync();
                this.OnGround = await stream.ReadBooleanAsync();
            }
        }

        public override async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MinecraftStream())
            {
                await stream.WriteDoubleAsync(this.Position.X);
                await stream.WriteDoubleAsync(this.Position.Y);
                await stream.WriteDoubleAsync(this.Position.Z);
                await stream.WriteBooleanAsync(this.OnGround);
                return stream.ToArray();
            }
        }
    }
}