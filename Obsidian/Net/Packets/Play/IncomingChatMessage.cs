using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    public class IncomingChatMessage : Packet
    {
        public IncomingChatMessage(byte[] data) : base(0x02, data) { }

        public string Message { get; private set; }


        public override async Task PopulateAsync()
        {
            using (var stream = new MinecraftStream(this.PacketData))
            {
                this.Message = await stream.ReadStringAsync(256);
            }
        }

        public override async Task<byte[]> ToArrayAsync()
        {
            using(var ms = new MinecraftStream())
            {
                await ms.WriteStringAsync(this.Message);
                return ms.ToArray();
            }
        }
    }
}