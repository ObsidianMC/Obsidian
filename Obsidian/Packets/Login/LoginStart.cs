using Obsidian.Util;
using System.Threading.Tasks;

namespace Obsidian.Packets
{
    public class LoginStart : Packet
    {
        public LoginStart(string username) : base(0x00, new byte[0]) => this.Username = username;
        public LoginStart(byte[] data) : base(0x00, data) { }

        public string Username { get; private set; }

        protected override async Task PopulateAsync()
        {
            using (var stream = new MinecraftStream(this._packetData))
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