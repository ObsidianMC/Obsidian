using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    public class Disconnect : Packet
    {
        readonly Chat.ChatMessage Reason;

        public Disconnect(Chat.ChatMessage reason, PacketState state) : base(state == PacketState.Play ? 0x1B : 0x00, new byte[0])
        {
            this.Reason = reason;
        }

        protected override async Task PopulateAsync()
        {
            await Task.Yield();
            throw new NotImplementedException();
        }

        public override async Task<byte[]> ToArrayAsync()
        {
            using(var stream = new MinecraftStream())
            {
                await stream.WriteChatAsync(this.Reason);
                return stream.ToArray();
            }
        }
    }
}