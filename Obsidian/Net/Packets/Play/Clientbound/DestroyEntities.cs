using Obsidian.Serialization.Attributes;
using System.Collections.Generic;
using System.Linq;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class DestroyEntities : IClientboundPacket
{
    [Field(0), VarLength]
    public List<int> Entities { get; private set; } = new();

    public int Id => 0x3A;

    public DestroyEntities(params int[] entities)
    {
        this.Entities = entities.ToList();
    }
}
