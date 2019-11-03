using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    public class IncomingChatMessage : Packet
    {
        public IncomingChatMessage(byte[] data) : base(0x02, data)
        {
        }

        public string Message { get; private set; }

        protected override async Task PopulateAsync(MinecraftStream stream) => Message = await stream.ReadStringAsync(256);

        protected override async Task ComposeAsync(MinecraftStream stream) => await stream.WriteStringAsync(this.Message);
    }
}