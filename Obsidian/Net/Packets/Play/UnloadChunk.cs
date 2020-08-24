using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play
{
    public class UnloadChunk : Packet
    {
        public int X { get; private set; }
        public int Z { get; private set; }

        public UnloadChunk(int x, int z) : base(0x1E, System.Array.Empty<byte>())
        {
            X = x;
            Z = z;
        }

        public UnloadChunk(byte[] data) : base(0x1E, data)
        {
        }

        protected override async Task ComposeAsync(MinecraftStream stream)
        {
            await stream.WriteIntAsync(X);
            await stream.WriteIntAsync(Z);
        }

        protected override async Task PopulateAsync(MinecraftStream stream)
        {
            X = await stream.ReadIntAsync();
            Z = await stream.ReadIntAsync();
        }
    }
}
