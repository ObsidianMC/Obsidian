namespace Obsidian.Net.Packets.Play
{
    public class HeldItemChange : Packet
    {
        public short Slot { get; }

        public HeldItemChange(short slot) : base(0x21)
        {
            this.Slot = slot;
        }

        public HeldItemChange(byte[] data) : base(0x21, data) { }
    }
}