using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using Obsidian.Util;

namespace Obsidian.Net.Packets.Handshaking
{
    public class Handshake : Packet
    {
        [Field(0, Type = DataType.VarInt)]
        public ProtocolVersion Version;

        [Field(1)]
        public string ServerAddress;

        [Field(2)]
        public ushort ServerPort;

        [Field(3, Type = DataType.VarInt)]
        public ClientState NextState;

        public Handshake() : base(0x00) { }

        public Handshake(byte[] data) : base(0x00, data) { }

    }
}