using Obsidian.Util;
using System;

namespace Obsidian.Net.Packets
{
    public class LoginSuccess : Packet
    {
        public LoginSuccess(Guid uuid, string username) : base(0x02, new byte[0])
        {
            this.Username = username;
            this.UUID = uuid;
        }

        [Variable]
        public Guid UUID { get; private set; } = Guid.Empty;

        [Variable]
        public string Username { get; private set; }
    }
}