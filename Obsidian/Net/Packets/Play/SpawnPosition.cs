using Obsidian.Util;

namespace Obsidian.Net.Packets
{
    public class SpawnPosition : Packet
    {
        public SpawnPosition(Position location) : base(0x49, new byte[0]) => Location = location;

        [Variable(VariableType.Position)]
        public Position Location { get; private set; }
    }
}