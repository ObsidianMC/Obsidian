namespace Obsidian.Net.Packets.Play.Clientbound;

/// <summary>
/// Stops the sounds a client plays: all of them, or those of a category, of an identifier, or both.
/// </summary>
public partial class StopSoundPacket
{
    public SoundCategory? Category { get; init; }

    public string? Sound { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        var flags = (this.Category is null ? 0 : 0x1) | (this.Sound is null ? 0 : 0x2);
        writer.WriteByte((sbyte)flags);

        if (this.Category is SoundCategory category)
            writer.WriteVarInt((int)category);

        if (this.Sound is not null)
            writer.WriteString(this.Sound);
    }
}
