using Obsidian.Entities;
using Obsidian.Util;
using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    public class PlayerPosition : Packet
    {
        public PlayerPosition(Position pos, bool onground) : base(0x10, Array.Empty<byte>())
        {
            this.Position = pos;
            this.OnGround = onground;
        }

        public PlayerPosition(byte[] data) : base(0x10, data)
        {
        }

        public Position Position { get; set; }

        public bool OnGround { get; private set; } = false;

        protected override async Task ComposeAsync(MinecraftStream stream)
        {
            await stream.WriteDoubleAsync(this.Position.X);
            await stream.WriteDoubleAsync(this.Position.Y);
            await stream.WriteDoubleAsync(this.Position.Z);
            await stream.WriteBooleanAsync(this.OnGround);
        }

        protected override async Task PopulateAsync(MinecraftStream stream)
        {
            this.Position = new Position
            {
                X = await stream.ReadDoubleAsync(),
                Y = await stream.ReadDoubleAsync(),
                Z = await stream.ReadDoubleAsync()
            };
            this.OnGround = await stream.ReadBooleanAsync();
        }
    }
}