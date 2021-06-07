using Obsidian.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class UpdateLight : IClientboundPacket
    {
        [Field(0), VarLength]
        public int ChunkX;

        [Field(1), VarLength]
        public int ChunkZ;

        [Field(2)]
        public bool TrustEdges = false;

        [Field(3), VarLength]
        public int SkyLightMask = 0b100000000000000000;

        [Field(4), VarLength]
        public int BlockLightMask = 0b100000000000000000;

        [Field(5), VarLength]
        public int EmptySkyLightMask = 0;

        [Field(6), VarLength]
        public int EmptyBlockLightMask = 0;

        // arrays have required sizes..
        [Field(8)]
        public byte[] SkyLightArrays = new byte[2048];

        [Field(10)]
        public byte[] BlockLightArrays = new byte[2048];

        public int Id => 0x23;
    }
}
