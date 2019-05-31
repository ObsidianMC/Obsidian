using System;
using System.IO;
using System.Threading.Tasks;

namespace Obsidian.Packets
{
    public class LoginSuccess : Packet
    {
        public LoginSuccess(Guid uuid, string username) : base(0x02, new byte[0])
        {
            this.Username = username;
            this.UUID = uuid;
        }

        public string Username { get; private set; }

        public Guid UUID { get; private set; } = Guid.Empty;

        protected override async Task PopulateAsync()
        {
            if (UUID != Guid.Empty || !string.IsNullOrEmpty(this.Username))
                return;

            using (var stream = new MemoryStream(this._packetData))
            {
                this.Username = await stream.ReadStringAsync();
                this.UUID = Guid.Parse(await stream.ReadStringAsync());
            }
        }

        public override async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MemoryStream())
            {
                await stream.WriteStringAsync(this.UUID.ToString());
                await stream.WriteStringAsync(this.Username);
                return stream.ToArray();
            }
        }

    }
}