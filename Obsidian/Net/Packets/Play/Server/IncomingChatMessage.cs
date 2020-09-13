using Obsidian.Serializer.Attributes;

namespace Obsidian.Net.Packets.Play.Server
{
    public class IncomingChatMessage : Packet
    {
        [Field(0)]
        public string Message { get; private set; }

        public IncomingChatMessage() : base(0x03) { }

        public IncomingChatMessage(byte[] data) : base(0x03, data) { }
    }
}