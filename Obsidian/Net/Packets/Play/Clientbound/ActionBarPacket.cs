using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class ActionBarPacket : IClientboundPacket
{
    [Field(0)]
    public string Text { get; init; }
    
    public int Id => 0x41;
}
