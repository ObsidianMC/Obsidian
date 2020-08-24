using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Login
{
    public class Disconnect : Packet
    {
        private readonly Chat.ChatMessage Reason;

        public Disconnect(Chat.ChatMessage reason, ClientState state) : base(state == ClientState.Play ? 0x1B : 0x00, Array.Empty<byte>())
        {
            this.Reason = reason;
        }

        protected override Task PopulateAsync(MinecraftStream stream) => throw new NotImplementedException();

        protected override async Task ComposeAsync(MinecraftStream stream) => await stream.WriteChatAsync(this.Reason);
    }
}