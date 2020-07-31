using Obsidian.Serializer.Attributes;

namespace Obsidian.Net.Packets.Play
{
    public class ClientSettings : Packet
    {
        [PacketOrder(0)]
        public string Locale { get; private set; }

        [PacketOrder(1)]
        public sbyte ViewDistance { get; private set; }

        [PacketOrder(2)]
        public int ChatMode { get; private set; }

        [PacketOrder(3)]
        public bool ChatColors { get; private set; }

        [PacketOrder(4)]
        public byte SkinParts { get; private set; } // skin parts that are displayed. might not be necessary to decode?

        [PacketOrder(5)]
        public int MainHand { get; private set; }

        public ClientSettings() : base(0x04) { }

        public ClientSettings(byte[] data) : base(0x04, data) { }   
    }
}