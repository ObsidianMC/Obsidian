using Obsidian.Serializer.Attributes;
using Obsidian.World;

namespace Obsidian.Net.Packets.Play
{
    public class ServerDifficulty
    {
        [PacketOrder(0)]
        public Difficulty Difficulty { get; }

        public ServerDifficulty(Difficulty difficulty) => this.Difficulty = difficulty;
    }
}