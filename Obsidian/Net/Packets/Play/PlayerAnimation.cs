using Obsidian.Util;

namespace Obsidian.Net.Packets.Play
{
    public class AnimationServerPacket : Packet
    {
        [Variable]
        public Hand Hand { get; set; }

        public AnimationServerPacket(byte[] data) : base(0x27, data) { }
    }

    public enum Hand
    {
        MainHand = 0,
        OffHand = 1
    }
}
