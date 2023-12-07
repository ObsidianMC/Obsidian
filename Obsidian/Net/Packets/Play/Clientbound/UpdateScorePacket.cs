using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class UpdateScorePacket : IClientboundPacket
{
    /// <summary>
    /// The entity whose score this is. For players, this is their username; for other entities, it is their UUID.
    /// </summary>
    [Field(0), FixedLength(40)]
    public string EntityName { get; init; }

    /// <summary>
    /// 0 to create/update an item. 1 to remove an item.
    /// </summary>
    [Field(1), VarLength]
    public int Action { get; init; }

    /// <summary>
    /// The name of the objective the score belongs to.
    /// </summary>
    [Field(3), FixedLength(16)]
    public string ObjectiveName { get; init; }

    /// <summary>
    /// The score to be displayed next to the entry. Only sent when Action does not equal 1.
    /// </summary>
    [Field(4), VarLength, Condition(nameof(Action) + " != 1")]
    public int Value { get; init; }

    public int Id => 0x5F;
}
