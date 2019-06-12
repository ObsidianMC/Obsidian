using Newtonsoft.Json;
using Obsidian.Chat;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    public class ChatMessagePacket : Packet
    {
        public ChatMessagePacket(ChatMessage message, byte position) : base(0x0E, new byte[0])
        {
            this.Message = message;
            this.Position = position;
        }

        public ChatMessage Message { get; private set; }

        public byte Position { get; private set; } = 0; // 0 = chatbox, 1 = system message, 2 = game info (actionbar)

        public override async Task PopulateAsync()
        {
            using (var stream = new MinecraftStream(this.PacketData))
            {
                this.Message = JsonConvert.DeserializeObject<ChatMessage>(await stream.ReadStringAsync());
                this.Position = await stream.ReadUnsignedByteAsync();
            }
        }

        public override async Task<byte[]> ToArrayAsync()
        {
            using(var stream = new MinecraftStream())
            {
                await stream.WriteStringAsync(this.Message.ToString());
                await stream.WriteUnsignedByteAsync(this.Position);
                return stream.ToArray();
            }
        }
    }
}