using System;
using System.Threading.Tasks;
using Obsidian.GameState;

namespace Obsidian.Net.Packets.Play
{
    public class ChangeGameState : Packet
    {
        public ChangeGameState(ChangeGameStateReason Reason) : base(0x20, Array.Empty<byte>()) => this.Reason = Reason;

        public ChangeGameStateReason Reason { get; private set; }

        protected override async Task ComposeAsync(MinecraftStream stream)
        {
            //NOTE: Uncomment if set should be made public
            //if (Reason == null) throw new Exception("Reason is null!");

            await stream.WriteUnsignedByteAsync(this.Reason.Reason);
            await stream.WriteFloatAsync(this.Reason.Value);
        }

        protected override Task PopulateAsync(MinecraftStream stream) => throw new NotImplementedException();
    }
}