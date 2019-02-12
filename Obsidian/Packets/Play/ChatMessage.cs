using Newtonsoft.Json;
using Obsidian.Entities;
using Obsidian.Packets.Handshaking;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Obsidian.Packets
{

    public class ChatMessage
    {
        public ChatMessage(Chat msg, byte position) // TODO: add constructor
        {
            this.Message = msg;
            this.Position = position;
        }

        public Chat Message { get; private set; }

        public byte Position { get; private set; } = 0; // 0 = chatbox, 1 = system message, 2 = game info (above hotbar)


        public static async Task<ChatMessage> FromArrayAsync(byte[] data)
        {
            MemoryStream stream = new MemoryStream(data);
            var chat = JsonConvert.DeserializeObject<Chat>(await stream.ReadStringAsync());
            var pos = await stream.ReadUnsignedByteAsync();
            return new ChatMessage(chat, pos);
        }

        public async Task<byte[]> ToArrayAsync()
        {
            MemoryStream stream = new MemoryStream();
            await stream.WriteStringAsync(JsonConvert.SerializeObject(this.Message));
            await stream.WriteUnsignedByteAsync(Position);
            return stream.ToArray();
        }
    }
}