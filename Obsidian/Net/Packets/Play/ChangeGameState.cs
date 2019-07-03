using Obsidian.GameState;
using Obsidian.Util;

namespace Obsidian.Net.Packets
{
    public class ChangeGameState : Packet
    {
        public ChangeGameState(ChangeGameStateReason Reason) : base(0x20, new byte[0]) => this.Reason = Reason;

        [Variable]
        public ChangeGameStateReason Reason { get; private set; }
    }
}
