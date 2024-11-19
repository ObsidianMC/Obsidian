using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class SetEntityDataPacket
{
    [Field(0), VarLength]
    public int EntityId { get; init; }

    [Field(1)]
    public Entity Entity { get; init; }
}
