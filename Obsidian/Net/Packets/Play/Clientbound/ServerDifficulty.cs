using Obsidian.Serialization.Attributes;
using Obsidian.WorldData;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class ServerDifficulty : IClientboundPacket
    {
        [Field(0), ActualType(typeof(byte))]
        public Difficulty Difficulty { get; }

        public int Id => 0x0E;

        public ServerDifficulty(Difficulty difficulty)
        {
            Difficulty = difficulty;
        }
    }
}
