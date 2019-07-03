using Obsidian.Chat;
using Obsidian.Util;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    public class Disconnect : Packet
    {
        [Variable]
        public readonly ChatMessage Reason;

        public Disconnect(ChatMessage reason, ClientState state) : base(state == ClientState.Play ? 0x1B : 0x00, new byte[0])
        {
            this.Reason = reason;
        }

        public override Task DeserializeAsync()
        {
            return Task.CompletedTask;
        }

    }
}