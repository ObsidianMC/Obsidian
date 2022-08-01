using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class SetBlockDestroyStagePacket : IClientboundPacket
{
    [Field(0), VarLength]
    public int EntityId { get; init; }

    [Field(1)]
    public VectorF Position { get; init; }

    /// <summary>
    /// 0-9 to set it, any other value to remove it.
    /// </summary>
    [Field(2)]
    public sbyte DestroyStage { get; init; }

    public int Id => 0x06;
}
