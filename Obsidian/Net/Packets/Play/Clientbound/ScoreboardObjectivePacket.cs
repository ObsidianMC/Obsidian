using Obsidian.API;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class ScoreboardObjectivePacket : IClientboundPacket
{
    [Field(0)]
    public string ObjectiveName { get; init; }

    [Field(1), ActualType(typeof(sbyte))]
    public ScoreboardMode Mode { get; init; }

    [Field(2), ActualType(typeof(ChatMessage)), Condition(nameof(ShouldWriteValue))]
    public ChatMessage Value { get; init; }

    [Field(3), VarLength, ActualType(typeof(int)), Condition(nameof(ShouldWriteValue))]
    public DisplayType Type { get; init; }

    public int Id => 0x53;

    private bool ShouldWriteValue => Mode is ScoreboardMode.Create or ScoreboardMode.Update;
}

public enum ScoreboardMode : sbyte
{
    Create,
    Remove,
    Update
}
