using Obsidian.GameState;
using Obsidian.Util;
using System;
using System.Threading.Tasks;

namespace Obsidian.Packets.Play
{
    public class ChangeGameState : Packet
    {
        public ChangeGameState(ChangeGameStateReason Reason) : base(0x20, new byte[0]) => this.Reason = Reason;

        public ChangeGameStateReason Reason { get; private set; }

        public override async Task<byte[]> ToArrayAsync()
        {
            //NOTE: Uncomment if set should be made public
            //if (Reason == null) throw new Exception("Reason is null!");

            using (var stream = new MinecraftStream())
            {
                await stream.WriteUnsignedByteAsync(this.Reason.Reason);
                await stream.WriteFloatAsync(this.Reason.Value);

                return stream.ToArray();
            }
        }

        protected override Task PopulateAsync() => throw new NotImplementedException();
    }
}
