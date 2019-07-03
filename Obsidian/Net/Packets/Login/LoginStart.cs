using Obsidian.Util;

namespace Obsidian.Net.Packets
{
    public class LoginStart : Packet
    {
        public LoginStart(string username) : base(0x00, new byte[0]) => this.Username = username;
        public LoginStart(byte[] data) : base(0x00, data) { }

        [Variable(VariableType.String)]
        public string Username { get; private set; }
    }
}