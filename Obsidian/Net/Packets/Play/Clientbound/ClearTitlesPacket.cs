using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;
public partial class ClearTitlesPacket 
{
    [Field(0)]
    public bool Reset { get; init; }

    public override void Serialize(INetStreamWriter writer) => writer.WriteBoolean(this.Reset);
}
