using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Login
{
    public class LoginSuccess : Packet
    {
        public LoginSuccess(string uuid, string username) : base(0x02, Array.Empty<byte>())
        {
            this.Username = username;
            this.UUID = uuid;
        }

        public string Username { get; private set; }

        public string UUID { get; private set; }

        protected override async Task PopulateAsync(MinecraftStream stream)
        {
            if (!string.IsNullOrEmpty(this.UUID) || !string.IsNullOrEmpty(this.Username))
                return;

            this.Username = await stream.ReadStringAsync();
            this.UUID = Guid.Parse(await stream.ReadStringAsync()).ToString();
        }

        protected override async Task ComposeAsync(MinecraftStream stream)
        {
            await stream.WriteStringAsync(this.UUID.ToString());
            await stream.WriteStringAsync(this.Username);
        }
    }
}