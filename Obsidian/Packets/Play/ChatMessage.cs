using Newtonsoft.Json;
using Obsidian.Entities;
using System.IO;
using System.Threading.Tasks;

namespace Obsidian.Packets
{

    public class ChatMessage : Packet
    {
        public ChatMessage(Entities.ChatMessage message, byte position) : base(0x0E, new byte[0])
        {
            this.Message = message;
            this.Position = position;
        }

        public Entities.ChatMessage Message { get; private set; }

        public byte Position { get; private set; } = 0; // 0 = chatbox, 1 = system message, 2 = game info (actionbar)

        protected override async Task PopulateAsync()
        {
            using(var stream = new MemoryStream(this._packetData))
            {
                this.Message = JsonConvert.DeserializeObject<Entities.ChatMessage>(await stream.ReadStringAsync());
                this.Position = await stream.ReadUnsignedByteAsync();
            }
        }

        public override async Task<byte[]> ToArrayAsync()
        {
            using(var stream = new MemoryStream())
            {
                await stream.WriteStringAsync(JsonConvert.SerializeObject(this.Message));
                await stream.WriteUnsignedByteAsync(this.Position);
                return stream.ToArray();
            }
        }
    }
}