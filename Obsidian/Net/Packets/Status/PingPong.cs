using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    public class PingPong : Packet
    {
        public long Payload;

        public PingPong(byte[] data) : base(0x01, data)
        {
        }

        protected override async Task PopulateAsync(MinecraftStream stream) => this.Payload = await stream.ReadLongAsync();

        protected override async Task ComposeAsync(MinecraftStream stream) => await stream.WriteLongAsync(this.Payload);
    }
}