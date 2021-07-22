using Obsidian.API;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class DisplayScoreboard : IClientboundPacket
    {
        [Field(0), ActualType(typeof(sbyte))]
        public ScoreboardPosition Position { get; init; }

        [Field(1)]
        public string ScoreName { get; init; }

        public int Id => 0x43;
    }
}
