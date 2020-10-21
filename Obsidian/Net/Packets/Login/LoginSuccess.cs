using Obsidian.Serializer.Attributes;
using System;

namespace Obsidian.Net.Packets.Login
{
    public class LoginSuccess : Packet
    {
        [Field(0)]
        public Guid UUID { get; set; }

        [Field(1)]
        public string Username { get; set; }

        public LoginSuccess(Guid uuid, string username) : base(0x02)
        {
            this.Username = username;
            this.UUID = uuid;
        }
    }
}