using Obsidian.Serializer.Attributes;

namespace Obsidian.Net.Packets.Play
{
    public class AnimationServerPacket : Packet
    {
        [Field(0)]
        public Hand Hand { get; set; }

        public AnimationServerPacket() : base(0x27) { }

        public AnimationServerPacket(byte[] data) : base(0x27, data) { }
    }

    public enum Hand
    {
        MainHand = 0,
        OffHand = 1
    }
}