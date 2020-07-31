using Obsidian.Chat;
using Obsidian.Serializer.Attributes;

namespace Obsidian.Net.Packets.Play
{
    public class ChatMessagePacket : Packet
    {
        [PacketOrder(0)]
        public ChatMessage Message { get; private set; }

        [PacketOrder(1)]
        public byte Position { get; private set; } // 0 = chatbox, 1 = system message, 2 = game info (actionbar)

        public ChatMessagePacket() : base(0x0E) { }

        public ChatMessagePacket(ChatMessage message, byte position) : base(0x0E)
        {
            this.Message = message;
            this.Position = position;
        }
    }
}