using Newtonsoft.Json;
using Obsidian.Chat;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    public class ChatMessagePacket : Packet
    {
        public ChatMessagePacket(ChatMessage message, byte position) : base(0x0E, System.Array.Empty<byte>())
        {
            this.Message = message;
            this.Position = position;
        }

        public ChatMessage Message { get; private set; }

        public byte Position { get; private set; } // 0 = chatbox, 1 = system message, 2 = game info (actionbar)

        protected override async Task PopulateAsync(MinecraftStream stream)
        {
            this.Message = JsonConvert.DeserializeObject<ChatMessage>(await stream.ReadStringAsync());
            this.Position = await stream.ReadUnsignedByteAsync();
        }

        protected override async Task ComposeAsync(MinecraftStream stream)
        {
            await stream.WriteStringAsync(this.Message.ToString());
            await stream.WriteUnsignedByteAsync(this.Position);
        }
    }
}