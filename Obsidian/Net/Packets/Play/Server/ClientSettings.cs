using Obsidian.Serializer.Attributes;

namespace Obsidian.Net.Packets.Play.Server
{
    public class ClientSettings : Packet
    {
        [Field(0)]
        public string Locale { get; private set; }

        [Field(1)]
        public sbyte ViewDistance { get; private set; }

        [Field(2)]
        public int ChatMode { get; private set; }

        [Field(3)]
        public bool ChatColors { get; private set; }

        [Field(4)]
        public byte SkinParts { get; private set; } // skin parts that are displayed. might not be necessary to decode?

        [Field(5)]
        public int MainHand { get; private set; }

        public ClientSettings() : base(0x05) { }

        public ClientSettings(byte[] data) : base(0x05, data) { }   
    }
}