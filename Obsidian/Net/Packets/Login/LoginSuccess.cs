using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Login
{
    public class LoginSuccess : Packet
    {
        public LoginSuccess(Guid uuid, string username) : base(0x02, Array.Empty<byte>())
        {
            this.Username = username;
            this.UUID = uuid;
        }

        public string Username { get; private set; }

        public Guid UUID { get; private set; } = Guid.Empty;

        protected override async Task PopulateAsync(MinecraftStream stream)
        {
            if (UUID != Guid.Empty || !string.IsNullOrEmpty(this.Username))
                return;

            this.Username = await stream.ReadStringAsync();
            this.UUID = Guid.Parse(await stream.ReadStringAsync());
        }

        protected override async Task ComposeAsync(MinecraftStream stream)
        {
            await stream.WriteStringAsync(this.UUID.ToString());
            await stream.WriteStringAsync(this.Username);
        }
    }
}