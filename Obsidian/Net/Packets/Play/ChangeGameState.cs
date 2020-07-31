using Obsidian.GameState;
using Obsidian.Serializer.Attributes;

namespace Obsidian.Net.Packets.Play
{
    public class ChangeGameState : Packet
    {
        [PacketOrder(0)]
        public ChangeGameStateReason Value { get; private set; }

        public ChangeGameState() : base(0x20) { }

        public ChangeGameState(ChangeGameStateReason Reason) : base(0x20) => this.Value = Reason;
    }
}