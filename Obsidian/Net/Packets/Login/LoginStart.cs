using Obsidian.Serializer.Attributes;

namespace Obsidian.Net.Packets.Login
{
    public class LoginStart : Packet
    {
        [Field(0)]
        public string Username { get; private set; }

        public LoginStart() : base(0x00) { }

        public LoginStart(byte[] data) : base(0x00, data) { }
    }
}