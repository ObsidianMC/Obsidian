using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using Obsidian.WorldData;

namespace Obsidian.Net.Packets.Play.Client
{
    public class ServerDifficulty : Packet
    {
        [Field(0, Type = DataType.UnsignedByte)]
        public Difficulty Difficulty { get; }

        public ServerDifficulty(Difficulty difficulty) : base(0x0E) => this.Difficulty = difficulty;
    }
}