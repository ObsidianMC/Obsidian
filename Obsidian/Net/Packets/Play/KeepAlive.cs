using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play
{
    public class KeepAlive : Packet
    {
        public KeepAlive(long id) : base(0x21, System.Array.Empty<byte>())
        {
            this.KeepAliveId = id;
        }

        public KeepAlive(byte[] data) : base(0x21, data)
        {
        }

        public long KeepAliveId { get; set; }

        protected override async Task PopulateAsync(MinecraftStream stream) => this.KeepAliveId = await stream.ReadLongAsync();

        protected override async Task ComposeAsync(MinecraftStream stream) => await stream.WriteLongAsync(this.KeepAliveId);
    }
}