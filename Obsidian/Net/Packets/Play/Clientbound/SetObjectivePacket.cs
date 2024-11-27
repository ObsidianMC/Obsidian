using Obsidian.Nbt;
using Obsidian.Net.Scoreboard;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class SetObjectivePacket
{
    [Field(0)]
    public required string ObjectiveName { get; init; }

    [Field(1), ActualType(typeof(sbyte))]
    public required ScoreboardMode Mode { get; init; }

    [Field(2), Condition(nameof(ShouldWriteValue))]
    public ChatMessage? Value { get; init; }

    [Field(3), VarLength, ActualType(typeof(int)), Condition(nameof(ShouldWriteValue))]
    public DisplayType Type { get; init; }

    [Field(5), ActualType(typeof(int)), VarLength, Condition("ShouldWriteValue")]
    public NumberFormat? NumberFormat { get; init; }

    [Field(6), Condition("NumberFormat == NumberFormat.Styled"), ActualType(typeof(NbtCompound))]
    public NbtCompound? StyledFormat { get; init; }

    [Field(6), Condition("NumberFormat == NumberFormat.Fixed"), ActualType(typeof(ChatMessage))]
    public ChatMessage? Content { get; init; }

    private bool ShouldWriteValue => Mode is ScoreboardMode.Create or ScoreboardMode.Update;

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteString(this.ObjectiveName);
        writer.WriteByte(this.Mode);

        if (this.ShouldWriteValue)
        {
            writer.WriteChat(this.Value!);
            writer.WriteByte(this.Type);

            writer.WriteBoolean(this.NumberFormat.HasValue);

            if(this.NumberFormat.HasValue)
            {
                writer.WriteVarInt(this.NumberFormat);

                if(this.NumberFormat == Scoreboard.NumberFormat.Styled)
                    ((MinecraftStream)writer).WriteNbtCompound(this.StyledFormat!);//API methods don't reference nbt.
                else if(this.NumberFormat == Scoreboard.NumberFormat.Fixed)
                    writer.WriteChat(this.Content!);

            }
        }
    }
}



