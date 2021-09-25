using Obsidian.API;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Login
{
    public partial class Disconnect : IClientboundPacket
    {
        [Field(0)]
        private ChatMessage Reason { get; }

        public int Id { get; }

        public Disconnect(ChatMessage reason, ClientState state)
        {
            Id = state == ClientState.Play ? 0x1A : 0x00;
            Reason = reason;
        }
    }
}
