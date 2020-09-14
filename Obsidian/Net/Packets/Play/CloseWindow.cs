using Obsidian.Serializer.Attributes;

namespace Obsidian.Net.Packets.Play
{
    public class CloseWindow : Packet
    {
        [Field(0)]
        public byte WindowId { get; set; }

        public CloseWindow() : base(0x0A) { }
    }
}
