using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    // Source: https://wiki.vg/index.php?title=Protocol#Update_View_Position
    public partial class UpdateViewPosition : IClientboundPacket
    {
        [Field(0), VarLength]
        public int ChunkX { get; }

        [Field(1), VarLength]
        public int ChunkZ { get; }

        public UpdateViewPosition(int chunkx, int chunkz)
        {
            ChunkX = chunkx;
            ChunkZ = chunkz;
        }

        public int Id => 0x40;
    }
}