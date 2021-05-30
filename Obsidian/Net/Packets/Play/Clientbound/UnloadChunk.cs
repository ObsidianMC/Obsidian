using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class UnloadChunk : IClientboundPacket
    {
        [Field(0)]
        public int X { get; }

        [Field(1)]
        public int Z { get; }

        public int Id => 0x1C;

        public UnloadChunk(int x, int z)
        {
            X = x;
            Z = z;
        }
    }
}
