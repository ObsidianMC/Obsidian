using System.IO;
using System.Threading.Tasks;

namespace Obsidian.Packets
{
    public class LoginSuccess : Packet
    {
        public LoginSuccess(string uuid, string username) : base(0x02, new byte[0])
        {
            this.Username = username;
            this.UUID = uuid;
        }

        public string Username { get; private set; }

        public string UUID { get; private set; }

        protected override async Task PopulateAsync()
        {
            if (!string.IsNullOrEmpty(this.UUID) || !string.IsNullOrEmpty(this.Username))
                return;

            using (var stream = new MemoryStream(this._packetData))
            {
                this.Username = await stream.ReadStringAsync();
                this.UUID = await stream.ReadStringAsync();
            }
        }

        public override async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MemoryStream())
            {
                await stream.WriteStringAsync(this.UUID);
                await stream.WriteStringAsync(this.Username);
                return stream.ToArray();
            }
        }

    }
}