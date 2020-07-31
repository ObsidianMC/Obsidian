using Obsidian.Serializer.Attributes;
using Obsidian.Util;

namespace Obsidian.Net.Packets.Handshaking
{
    public class Handshake : Packet
    {
        [PacketOrder(0)]
        public ProtocolVersion Version;

        [PacketOrder(1)]
        public string ServerAddress;

        [PacketOrder(2)]
        public ushort ServerPort;

        [PacketOrder(3)]
        public ClientState NextState;

        public Handshake() : base(0x00) { }

        public Handshake(byte[] data) : base(0x00, data) { }

    }
}