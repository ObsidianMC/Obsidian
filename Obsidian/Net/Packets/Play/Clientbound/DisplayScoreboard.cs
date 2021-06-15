using Obsidian.API;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class DisplayScoreboard : IClientboundPacket
    {
        [Field(0), ActualType(typeof(sbyte))]
        public ScoreboardPosition Position { get; set; }

        [Field(1)]
        public string ScoreName { get; set; }

        public int Id => 0x43;
    }
}
