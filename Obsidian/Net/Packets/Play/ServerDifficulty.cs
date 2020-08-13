using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using Obsidian.World;

namespace Obsidian.Net.Packets.Play
{
    public class ServerDifficulty
    {
        [Field(0, Type = DataType.UnsignedByte)]
        public Difficulty Difficulty { get; }

        public ServerDifficulty(Difficulty difficulty) => this.Difficulty = difficulty;
    }
}