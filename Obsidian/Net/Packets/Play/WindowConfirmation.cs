using Obsidian.Serializer.Attributes;

namespace Obsidian.Net.Packets.Play
{
    public class WindowConfirmation : Packet
    {
        [Field(0)]
        public sbyte WindowId { get; set; }

        [Field(1)]
        public short ActionNumber { get; set; }

        [Field(2)]
        public bool Accepted { get; set; }

        public WindowConfirmation() : base(0x13) { }
    }
}
