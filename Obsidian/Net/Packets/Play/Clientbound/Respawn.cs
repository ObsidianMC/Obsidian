using Obsidian.API;
using Obsidian.Nbt;
using Obsidian.Serialization.Attributes;
using Obsidian.Utilities.Registry.Codecs.Dimensions;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class Respawn : IClientboundPacket
    {
        [Field(0)]
        public DimensionCodec Dimension { get; set; }

        [Field(1)]
        public string WorldName { get; set; }

        [Field(2)]
        public long HashedSeed { get; set; }

        [Field(3), ActualType(typeof(byte))]
        public Gamemode Gamemode { get; set; }

        [Field(4), ActualType(typeof(byte))]
        public Gamemode PreviousGamemode { get; set; }

        [Field(5)]
        public bool IsDebug { get; set; }

        [Field(6)]
        public bool IsFlat { get; set; }

        [Field(7)]
        public bool CopyMetadata { get; set; }

        public int Id => 0x39;
    }
}
