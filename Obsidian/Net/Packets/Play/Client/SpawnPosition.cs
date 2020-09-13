using Obsidian.Serializer.Attributes;
using Obsidian.Util.DataTypes;

namespace Obsidian.Net.Packets.Play.Client
{
    public class SpawnPosition : Packet
    {
        [Field(0)]
        public Position Location { get; private set; }

        public SpawnPosition(Position location) : base(0x4E) => Location = location;
    }
}