using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    public class LoginStart : Packet
    {
        public LoginStart(string username) : base(0x00, System.Array.Empty<byte>()) => this.Username = username;

        public LoginStart(byte[] data) : base(0x00, data)
        {
        }

        public string Username { get; private set; }

        protected override async Task PopulateAsync(MinecraftStream stream) => this.Username = await stream.ReadStringAsync();

        protected override async Task ComposeAsync(MinecraftStream stream) => await stream.WriteStringAsync(this.Username);
    }
}