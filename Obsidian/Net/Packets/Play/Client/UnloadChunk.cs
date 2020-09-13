using Obsidian.Serializer.Attributes;

namespace Obsidian.Net.Packets.Play.Client
{
    public class UnloadChunk : Packet
    {
        [Field(0, true)]
        public int X { get; private set; }

        [Field(1, true)]
        public int Z { get; private set; }

        public UnloadChunk(byte[] data) : base(0x1E, data) { }

        public UnloadChunk(int x, int z) : base(0x1E)
        {
            this.X = x;
            this.Z = z;
        }
    }
}
