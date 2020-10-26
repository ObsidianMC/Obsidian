using Obsidian.Serializer.Attributes;

namespace Obsidian.Net.Packets.Play
{
    public class Animation : Packet
    {
        [Field(0, Type = Serializer.Enums.DataType.VarInt)]
        public Hand Hand { get; set; }

        public Animation() : base(0x2C) { }

        public Animation(byte[] data) : base(0x2C, data) { }
    }

    public enum Hand : int
    {
        MainHand = 0,
        OffHand = 1
    }
}