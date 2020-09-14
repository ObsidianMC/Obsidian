using Obsidian.Serializer.Attributes;

namespace Obsidian.Net.Packets.Play.Server
{
    public class ClickWindowButton : Packet
    {
        [Field(0)]
        public sbyte WindowId { get; set; }

        [Field(1)]
        public sbyte ButtonId { get; set; }

        public ClickWindowButton() : base(0x08) { }

    }
}
