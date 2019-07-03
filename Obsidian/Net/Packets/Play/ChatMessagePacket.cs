using Obsidian.Chat;
using Obsidian.Util;

namespace Obsidian.Net.Packets
{
    public class ChatMessagePacket : Packet
    {
        public ChatMessagePacket(ChatMessage message, byte position) : base(0x0E, new byte[0])
        {
            this.Message = message;
            this.Position = position;
        }

        [Variable]
        public ChatMessage Message { get; private set; }

        [Variable]
        public byte Position { get; private set; } = 0; // 0 = chatbox, 1 = system message, 2 = game info (actionbar)
    }
}