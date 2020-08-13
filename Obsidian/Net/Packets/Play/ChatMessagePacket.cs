using Obsidian.Chat;
using Obsidian.Serializer.Attributes;

namespace Obsidian.Net.Packets.Play
{
    public class ChatMessagePacket : Packet
    {
        [Field(0)]
        public ChatMessage Message { get; private set; }

        [Field(1)]
        public sbyte Position { get; private set; } // 0 = chatbox, 1 = system message, 2 = game info (actionbar)

        public ChatMessagePacket() : base(0x0E) { }

        public ChatMessagePacket(ChatMessage message, sbyte position) : base(0x0E)
        {
            this.Message = message;
            this.Position = position;
        }
    }
}