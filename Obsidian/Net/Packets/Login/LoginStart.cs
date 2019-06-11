using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    public class LoginStart : Packet
    {
        public LoginStart(string username) : base(0x00, new byte[0]) => this.Username = username;
        public LoginStart(byte[] data) : base(0x00, data) { }

        public string Username { get; private set; }

        public override async Task PopulateAsync()
        {
            using (var stream = new MinecraftStream(this.PacketData))
            {
                this.Username = await stream.ReadStringAsync();
            }
        }

        public override async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MinecraftStream())
            {
                await stream.WriteStringAsync(this.Username);
                return stream.ToArray();
            }
        }
    }
}