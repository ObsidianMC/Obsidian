using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class UpdateLight : IClientboundPacket
    {
        [Field(0), VarLength]
        public int ChunkX { get; init; }

        [Field(1), VarLength]
        public int ChunkZ { get; init; }

        [Field(2)]
        public bool TrustEdges { get; init; }

        [Field(3), VarLength]
        public int SkyLightMask { get; init; }

        [Field(4), VarLength]
        public int BlockLightMask { get; init; }

        [Field(5), VarLength]
        public int EmptySkyLightMask { get; init; }

        [Field(6), VarLength]
        public int EmptyBlockLightMask { get; init; }

        /// <summary>
        /// One array for each bit set to true in the sky light mask, a nibble per light value. Length has to be 2048.
        /// </summary>
        [Field(8)]
        public byte[] SkyLightArrays { get; init; }

        /// <summary>
        /// One array for each bit set to true in the block light mask, a nibble per light value. Length has to be 2048.
        /// </summary>
        [Field(10)]
        public byte[] BlockLightArrays { get; init; }

        public int Id => 0x25;
    }
}
