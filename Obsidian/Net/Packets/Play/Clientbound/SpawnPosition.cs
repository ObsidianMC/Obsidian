using Obsidian.API;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class SpawnPosition : IClientboundPacket
    {
        [Field(0)]
        public VectorF Position { get; }

        public int Id => 0x42;

        public SpawnPosition(VectorF position)
        {
            Position = position;
        }
    }
}
