using Obsidian.API;
using Obsidian.Serialization.Attributes;
using Obsidian.Utilities.Registry.Codecs.Dimensions;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class Respawn : IClientboundPacket
    {
        [Field(0)]
        public DimensionCodec Dimension { get; init; }

        [Field(1)]
        public string WorldName { get; init; }

        [Field(2)]
        public long HashedSeed { get; init; }

        [Field(3), ActualType(typeof(byte))]
        public Gamemode Gamemode { get; init; }

        [Field(4), ActualType(typeof(byte))]
        public Gamemode PreviousGamemode { get; init; }

        [Field(5)]
        public bool IsDebug { get; init; }

        [Field(6)]
        public bool IsFlat { get; init; }

        [Field(7)]
        public bool CopyMetadata { get; init; }

        public int Id => 0x39;
    }
}
