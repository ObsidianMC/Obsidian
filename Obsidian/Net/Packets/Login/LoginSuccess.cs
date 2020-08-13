using Obsidian.Serializer.Attributes;

namespace Obsidian.Net.Packets.Login
{
    public class LoginSuccess : Packet
    {
        [Field(0)]
        public string UUID { get; set; }

        [Field(1)]
        public string Username { get; set; }

        public LoginSuccess(string uuid, string username) : base(0x02)
        {
            this.Username = username;
            this.UUID = uuid;
        }
    }
}