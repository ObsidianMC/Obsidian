using Obsidian.API;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class SpawnExperienceOrb : IClientboundPacket
    {
        [Field(0), VarLength]
        private const int entityId = 2; // Source: https://minecraft.gamepedia.com/Java_Edition_data_values/Pre-flattening/Entity_IDs

        [Field(1), VectorFormat(typeof(double))]
        public VectorF Position { get; }

        [Field(2)]
        public short Count { get; }

        public int Id => 0x01;

        public SpawnExperienceOrb(short count, VectorF position)
        {
            Count = count;
            Position = position;
        }
    }
}
