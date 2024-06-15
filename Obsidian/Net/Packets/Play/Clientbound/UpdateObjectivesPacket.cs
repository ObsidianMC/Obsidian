using Obsidian.Nbt;
using Obsidian.Net.Scoreboard;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class UpdateObjectivesPacket : IClientboundPacket
{
    [Field(0)]
    public required string ObjectiveName { get; init; }

    [Field(1), ActualType(typeof(sbyte))]
    public required ScoreboardMode Mode { get; init; }

    [Field(2), Condition(nameof(ShouldWriteValue)), ActualType(typeof(ChatMessage))]
    public ChatMessage? Value { get; init; }

    [Field(3), VarLength, ActualType(typeof(int)), Condition(nameof(ShouldWriteValue))]
    public DisplayType Type { get; init; }

    [Field(4), Condition(nameof(ShouldWriteValue))]
    public bool HasNumberFormat { get; init; }

    [Field(5), ActualType(typeof(int)), VarLength, Condition("ShouldWriteValue && HasNumberFormat")]
    public NumberFormat NumberFormat { get; init; }

    [Field(6), Condition("NumberFormat == NumberFormat.Styled"), ActualType(typeof(NbtCompound))]
    public NbtCompound? StyledFormat { get; init; }

    [Field(6), Condition("NumberFormat == NumberFormat.Fixed"), ActualType(typeof(ChatMessage))]
    public ChatMessage? Content { get; init; }

    public int Id => 0x5E;

    private bool ShouldWriteValue => Mode is ScoreboardMode.Create or ScoreboardMode.Update;
}



