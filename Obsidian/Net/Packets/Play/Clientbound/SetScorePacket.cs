using Obsidian.Nbt;
using Obsidian.Net.Scoreboard;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class SetScorePacket
{
    /// <summary>
    /// The entity whose score this is. For players, this is their username; for other entities, it is their UUID.
    /// </summary>
    [Field(0)]
    public required string EntityName { get; init; }

    /// <summary>
    /// The name of the objective the score belongs to.
    /// </summary>
    [Field(1)]
    public required string ObjectiveName { get; init; }

    /// <summary>
    /// The score to be displayed next to the entry. Only sent when Action does not equal 1.
    /// </summary>
    [Field(2), VarLength]
    public int Value { get; init; }

    [Field(4), ActualType(typeof(ChatMessage))]
    public ChatMessage? DisplayName { get; init; }

    [Field(6), ActualType(typeof(int)), VarLength]
    public NumberFormat? NumberFormat { get; init; }

    [Field(7), Condition("NumberFormat == NumberFormat.Styled"), ActualType(typeof(NbtCompound))]
    public NbtCompound? StyledFormat { get; init; }

    [Field(7), Condition("NumberFormat == NumberFormat.Fixed"), ActualType(typeof(ChatMessage))]
    public ChatMessage? Content { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteString(this.EntityName);
        writer.WriteString(this.ObjectiveName);
        writer.WriteVarInt(this.Value);

        writer.WriteOptional(this.DisplayName);

        writer.WriteOptional(this.NumberFormat);

        if (this.NumberFormat == Scoreboard.NumberFormat.Styled)
            ((MinecraftStream)writer).WriteNbtCompound(this.StyledFormat!);

        if (this.NumberFormat == Scoreboard.NumberFormat.Fixed)
            writer.WriteChat(this.Content!);
    }
}
