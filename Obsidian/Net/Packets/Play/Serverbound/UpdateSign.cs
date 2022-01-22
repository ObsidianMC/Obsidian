using Microsoft.Extensions.Logging;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class UpdateSign : IServerboundPacket
{
    [Field(0)]
    public VectorF Location { get; set; }

    [Field(1), FixedLength(384)]
    public string Line1 { get; set; }
    
    [Field(2), FixedLength(384)]
    public string Line2 { get; set; }
    
    [Field(3), FixedLength(384)]
    public string Line3 { get; set; }
    
    [Field(4), FixedLength(384)]
    public string Line4 { get; set; }

    public int Id => 0x2B;

    public ValueTask HandleAsync(Server server, Player player) => ValueTask.CompletedTask;
}
