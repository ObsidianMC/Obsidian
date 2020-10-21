using Obsidian.Chat;
using Obsidian.Serializer.Attributes;

namespace Obsidian.Net.Packets.Login
{
    public class Disconnect : Packet
    {
        [Field(0)]
        private readonly ChatMessage Reason;

        public Disconnect(ChatMessage reason, ClientState state) : base(state == ClientState.Play ? 0x19 : 0x00)
        {
            this.Reason = reason;
        }
    }
}