using Obsidian.Serializer.Attributes;

namespace Obsidian.Net.Packets.Login
{
    public class SetCompression : Packet
    {
        [PacketOrder(0)]
        public int Threshold { get; }

        public bool Enabled => Threshold < 0;

        public SetCompression(int threshold) : base(0x03)
        {
            this.Threshold = threshold;
        }
    }
}
